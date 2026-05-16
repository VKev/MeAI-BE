using System.Security.Cryptography;
using System.Text;
using Application.Abstractions.Facebook;
using Application.Abstractions.Rag;
using Application.Abstractions.SocialMedias;
using Application.Posts;
using Application.Posts.Models;
using Application.Posts.Queries;
using Application.Recommendations.Models;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Recommendations.Commands;

public sealed record IndexSocialAccountPostsCommand(
    Guid UserId,
    Guid SocialMediaId,
    int? MaxPosts = null,
    Func<IndexSocialAccountIngestFailureBatch, CancellationToken, Task>? OnIngestFailures = null,
    Func<IndexSocialAccountReadBatch, CancellationToken, Task>? OnReadBatch = null,
    bool StopOnProviderCreditFailure = false)
    : IRequest<Result<IndexSocialAccountPostsResponse>>;

public sealed class IndexSocialAccountPostsCommandHandler
    : IRequestHandler<IndexSocialAccountPostsCommand, Result<IndexSocialAccountPostsResponse>>
{
    private const int PageSize = 25;
    private const int DefaultMaxPosts = 200;
    private const int HardCapPosts = 2000;
    private const int RagIngestBatchSize = 3;

    private const string ImageDescribePrompt =
        "Describe this social media post image so it can be retrieved by semantic search and used " +
        "to recommend similar future content. Cover: subject, brand or logo, visible text/OCR, " +
        "color palette, mood, and visual style. Be concise but specific.";

    private readonly IMediator _mediator;
    private readonly IRagClient _ragClient;
    private readonly IUserSocialMediaService _userSocialMediaService;
    private readonly IFacebookContentService _facebookContentService;
    private readonly ILogger<IndexSocialAccountPostsCommandHandler> _logger;

    public IndexSocialAccountPostsCommandHandler(
        IMediator mediator,
        IRagClient ragClient,
        IUserSocialMediaService userSocialMediaService,
        IFacebookContentService facebookContentService,
        ILogger<IndexSocialAccountPostsCommandHandler> logger)
    {
        _mediator = mediator;
        _ragClient = ragClient;
        _userSocialMediaService = userSocialMediaService;
        _facebookContentService = facebookContentService;
        _logger = logger;
    }

    public async Task<Result<IndexSocialAccountPostsResponse>> Handle(
        IndexSocialAccountPostsCommand request,
        CancellationToken cancellationToken)
    {
        var maxPosts = Math.Clamp(request.MaxPosts ?? DefaultMaxPosts, 1, HardCapPosts);

        var posts = new List<SocialPlatformPostSummaryResponse>();
        string? platform = null;
        string? cursor = null;

        while (posts.Count < maxPosts)
        {
            var pageLimit = Math.Min(PageSize, maxPosts - posts.Count);
            var pageResult = await _mediator.Send(
                new GetSocialMediaPlatformPostsQuery(
                    request.UserId,
                    request.SocialMediaId,
                    cursor,
                    pageLimit),
                cancellationToken);

            if (pageResult.IsFailure)
            {
                if (posts.Count == 0)
                {
                    return Result.Failure<IndexSocialAccountPostsResponse>(pageResult.Error);
                }

                _logger.LogWarning(
                    "Mid-iteration page fetch failed for socialMediaId={SocialMediaId}; indexing what we already have ({Count} posts). Error: {Code} {Description}",
                    request.SocialMediaId,
                    posts.Count,
                    pageResult.Error.Code,
                    pageResult.Error.Description);
                break;
            }

            var page = pageResult.Value;
            platform ??= page.Platform;
            posts.AddRange(page.Items);

            if (!page.HasMore || string.IsNullOrEmpty(page.NextCursor) || page.Items.Count == 0)
            {
                break;
            }

            cursor = page.NextCursor;
        }

        if (platform is null)
        {
            return Result.Failure<IndexSocialAccountPostsResponse>(
                new Error("Recommendations.NoPlatform", "Could not resolve social media platform."));
        }

        var prefix = BuildPrefix(platform, request.SocialMediaId);
        var existing = await _ragClient.ListFingerprintsAsync(prefix, cancellationToken);

        var docsToQueue = new List<RagIngestMessage>(capacity: posts.Count * 2);
        var newPosts = 0;
        var updatedPosts = 0;
        var unchangedPosts = 0;
        var queuedText = 0;
        var queuedImage = 0;
        var queuedVideo = 0;
        var queuedProfile = 0;
        var failedIngestDocuments = new List<IndexSocialAccountIngestFailure>();
        var readItemsByDocumentId = new Dictionary<string, IndexSocialAccountPostReadItem>(StringComparer.Ordinal);

        // Page profile (the "Giới thiệu" / About section + category + website + location).
        // Ingested as a single text doc per account so the recommendation/draft LLM can
        // ground generated content in what the page is actually about — not just past
        // posts. Fingerprint-skipped on subsequent /index calls when the profile hasn't changed.
        var profileDoc = await BuildPageProfileDocAsync(platform, request, cancellationToken);
        if (profileDoc is not null)
        {
            var profileDocId = $"{prefix}profile";
            var profileStatus = Classify(existing, profileDocId, profileDoc.Value.Fingerprint);
            if (profileStatus != DocStatus.Unchanged)
            {
                docsToQueue.Add(new RagIngestMessage
                {
                    Kind = "text",
                    DocumentId = profileDocId,
                    Fingerprint = profileDoc.Value.Fingerprint,
                    Content = profileDoc.Value.Content,
                });
                readItemsByDocumentId[profileDocId] = BuildProfileReadItem();
                queuedProfile++;
            }
        }

        foreach (var post in posts)
        {
            if (string.IsNullOrWhiteSpace(post.PlatformPostId))
            {
                continue;
            }

            var textDocId = $"{prefix}{post.PlatformPostId}";
            var (textContent, textFingerprint) = BuildTextDoc(platform, post);
            var textStatus = Classify(existing, textDocId, textFingerprint);

            switch (textStatus)
            {
                case DocStatus.New: newPosts++; break;
                case DocStatus.Updated: updatedPosts++; break;
                case DocStatus.Unchanged: unchangedPosts++; break;
            }

            if (textStatus != DocStatus.Unchanged)
            {
                docsToQueue.Add(new RagIngestMessage
                {
                    Kind = "text",
                    DocumentId = textDocId,
                    Fingerprint = textFingerprint,
                    Content = textContent,
                });
                readItemsByDocumentId[textDocId] = BuildPostReadItem(post, "text");
                queuedText++;
            }

            var imageUrl = SelectImageUrl(post);
            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                var caption = string.IsNullOrWhiteSpace(post.Text) ? post.Title : post.Text;

                // Path 1: vision-LLM describe-then-text-embed (for the LLM context).
                var imageDocId = $"{prefix}{post.PlatformPostId}:img:0";
                var imageFingerprint = ComputeFingerprint(imageUrl!);
                var imageStatus = Classify(existing, imageDocId, imageFingerprint);

                if (imageStatus != DocStatus.Unchanged)
                {
                    docsToQueue.Add(new RagIngestMessage
                    {
                        Kind = "image",
                        DocumentId = imageDocId,
                        Fingerprint = imageFingerprint,
                        ImageUrl = imageUrl,
                        Caption = caption,
                        DescribePrompt = ImageDescribePrompt,
                    });
                    readItemsByDocumentId[imageDocId] = BuildPostReadItem(post, "image description");
                    queuedImage++;
                }

                // Path 2: native multimodal embedding (image vector + caption vector
                // in the same space) so text queries can retrieve the image directly.
                // Suffix v2 = Gemini Embedding 2 Preview (3072-dim) collection.
                var visualDocId = $"{prefix}{post.PlatformPostId}:vis2:0";
                var visualFingerprint = ComputeFingerprint(
                    string.Concat(imageUrl, "|", caption ?? string.Empty));
                var visualStatus = Classify(existing, visualDocId, visualFingerprint);

                if (visualStatus != DocStatus.Unchanged)
                {
                    docsToQueue.Add(new RagIngestMessage
                    {
                        Kind = "image_native",
                        DocumentId = visualDocId,
                        Fingerprint = visualFingerprint,
                        ImageUrl = imageUrl,
                        Caption = caption,
                        Scope = prefix,
                        PostId = post.PlatformPostId,
                    });
                    readItemsByDocumentId[visualDocId] = BuildPostReadItem(post, "image embedding");
                    queuedImage++;
                }
            }

            // Path 3: VideoRAG — only fires when the post is a video. Heavier
            // than image_native (download + ffmpeg + multi-frame Gemini calls)
            // so we gate strictly on MediaType + a fetchable video URL.
            var videoUrl = SelectVideoUrl(post);
            if (videoUrl is not null)
            {
                var videoDocId = $"{prefix}{post.PlatformPostId}:vid:0";
                var videoFingerprint = ComputeFingerprint(
                    string.Concat(
                        post.PlatformPostId,
                        "|",
                        post.MediaType,
                        "|",
                        post.PublishedAt?.ToUnixTimeSeconds()));
                var videoStatus = Classify(existing, videoDocId, videoFingerprint);

                if (videoStatus != DocStatus.Unchanged)
                {
                    docsToQueue.Add(new RagIngestMessage
                    {
                        Kind = "video",
                        DocumentId = videoDocId,
                        Fingerprint = videoFingerprint,
                        VideoUrl = videoUrl,
                        Platform = platform,
                        SocialMediaId = request.SocialMediaId.ToString("N"),
                        PostId = post.PlatformPostId,
                        Scope = prefix,
                    });
                    readItemsByDocumentId[videoDocId] = BuildPostReadItem(post, "video");
                    queuedVideo++;
                }
            }
        }

        if (docsToQueue.Count > 0)
        {
            // Synchronous batch ingest via gRPC. Blocks until rag-microservice has
            // actually embedded + upserted every doc into Qdrant + the fingerprint
            // registry — so the recommendation/draft query that runs immediately
            // after IS guaranteed to see the page profile and post embeddings. The
            // earlier fire-and-forget path (PublishIngestBatchAsync via RabbitMQ)
            // raced with the downstream query and left RAG context empty.
            _logger.LogInformation(
                "Submitting {DocCount} RAG ingest documents (sync gRPC) for socialMediaId={SocialMediaId} (text={Text}, image={Image}, video={Video}, profile={Profile})",
                docsToQueue.Count,
                request.SocialMediaId,
                queuedText,
                queuedImage,
                queuedVideo,
                queuedProfile);

            var ingested = 0; var unchanged = 0; var failed = 0;
            foreach (var batch in docsToQueue.Chunk(RagIngestBatchSize))
            {
                if (request.OnReadBatch is not null)
                {
                    var postsInBatch = BuildReadBatchItems(batch, readItemsByDocumentId);
                    if (postsInBatch.Count > 0)
                    {
                        await request.OnReadBatch(
                            new IndexSocialAccountReadBatch(
                                request.SocialMediaId,
                                platform,
                                prefix,
                                postsInBatch),
                            cancellationToken);
                    }
                }

                var ingestResults = await _ragClient.IngestBatchSyncAsync(batch, cancellationToken);
                var batchFailures = new List<IndexSocialAccountIngestFailure>();

                foreach (var r in ingestResults)
                {
                    switch ((r.Status ?? string.Empty).ToLowerInvariant())
                    {
                        case "ingested":
                        case "updated":  ingested++; break;
                        case "unchanged": unchanged++; break;
                        case "failed":
                            failed++;
                            var failure = new IndexSocialAccountIngestFailure(r.DocumentId, r.Error);
                            failedIngestDocuments.Add(failure);
                            batchFailures.Add(failure);
                            _logger.LogWarning(
                                "RAG ingest failed for {DocId}: {Error}", r.DocumentId, r.Error);
                            break;
                    }
                }

                if (batchFailures.Count > 0 && request.OnIngestFailures is not null)
                {
                    await request.OnIngestFailures(
                        new IndexSocialAccountIngestFailureBatch(
                            request.SocialMediaId,
                            platform,
                            prefix,
                            batchFailures),
                        cancellationToken);
                }

                if (request.StopOnProviderCreditFailure && batchFailures.Any(item => LooksLikeProviderCreditFailure(item.Error)))
                {
                    _logger.LogWarning(
                        "Stopping RAG ingest early for socialMediaId={SocialMediaId} because the provider reported insufficient credits.",
                        request.SocialMediaId);
                    break;
                }
            }
            _logger.LogInformation(
                "RAG sync ingest completed: ingested/updated={Ingested}, unchanged={Unchanged}, failed={Failed}",
                ingested, unchanged, failed);
        }
        else
        {
            _logger.LogInformation(
                "No RAG ingest needed for socialMediaId={SocialMediaId} — all {Count} posts up to date",
                request.SocialMediaId,
                posts.Count);
        }

        return Result.Success(new IndexSocialAccountPostsResponse(
            SocialMediaId: request.SocialMediaId,
            Platform: platform,
            DocumentIdPrefix: prefix,
            TotalPostsScanned: posts.Count,
            NewPosts: newPosts,
            UpdatedPosts: updatedPosts,
            UnchangedPosts: unchangedPosts,
            QueuedTextDocuments: queuedText,
            QueuedImageDocuments: queuedImage,
            QueuedVideoDocuments: queuedVideo,
            QueuedProfileDocuments: queuedProfile,
            FailedIngestDocuments: failedIngestDocuments));
    }

    /// <summary>
    /// Fetches the page profile (About / category / website / location / bio) for the
    /// given account and renders it as a single fingerprinted text doc. Returns null
    /// for unsupported platforms or when the profile fetch fails — RAG ingest of
    /// posts/images still proceeds without it.
    /// </summary>
    private async Task<(string Content, string Fingerprint)?> BuildPageProfileDocAsync(
        string platform,
        IndexSocialAccountPostsCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(platform, "facebook", StringComparison.OrdinalIgnoreCase))
        {
            // Instagram / TikTok / Threads: their profile data is in social_medias.metadata
            // (set at OAuth link time). A future extension can plumb those through the same
            // shape; for now only Facebook ships rich Graph-API profile data.
            return null;
        }

        var socialMediaResult = await _userSocialMediaService.GetSocialMediasAsync(
            request.UserId, new[] { request.SocialMediaId }, cancellationToken);
        if (socialMediaResult.IsFailure || socialMediaResult.Value.Count == 0)
        {
            return null;
        }

        using var metadata = SocialMediaMetadataHelper.Parse(socialMediaResult.Value[0].MetadataJson);
        if (metadata is null)
        {
            return null;
        }

        var userAccessToken = SocialMediaMetadataHelper.GetString(metadata, "user_access_token")
                              ?? SocialMediaMetadataHelper.GetString(metadata, "access_token");
        if (string.IsNullOrWhiteSpace(userAccessToken))
        {
            return null;
        }

        var insightsResult = await _facebookContentService.GetPageInsightsAsync(
            new FacebookPageInsightsRequest(
                UserAccessToken: userAccessToken,
                PreferredPageId: SocialMediaMetadataHelper.GetString(metadata, "page_id"),
                PreferredPageAccessToken: SocialMediaMetadataHelper.GetString(metadata, "page_access_token")),
            cancellationToken);
        if (insightsResult.IsFailure)
        {
            _logger.LogWarning(
                "Page profile fetch failed for socialMediaId={SocialMediaId}: {Code} {Description}",
                request.SocialMediaId, insightsResult.Error.Code, insightsResult.Error.Description);
            return null;
        }

        var p = insightsResult.Value;
        var sb = new StringBuilder();
        sb.AppendLine($"[Page profile — facebook account {request.SocialMediaId:N}]");
        if (!string.IsNullOrWhiteSpace(p.Name))         sb.AppendLine($"Name: {p.Name}");
        if (!string.IsNullOrWhiteSpace(p.Category))     sb.AppendLine($"Category: {p.Category}");
        if (p.Followers.HasValue)                        sb.AppendLine($"Followers: {p.Followers.Value:N0}");
        if (p.Fans.HasValue)                             sb.AppendLine($"Likes (fans): {p.Fans.Value:N0}");
        if (!string.IsNullOrWhiteSpace(p.About))        sb.AppendLine($"Tagline: {p.About}");
        if (!string.IsNullOrWhiteSpace(p.Description))
        {
            sb.AppendLine("Introduction (Giới thiệu):");
            sb.AppendLine(p.Description.Trim());
        }
        if (!string.IsNullOrWhiteSpace(p.Bio))           sb.AppendLine($"Bio: {p.Bio.Trim()}");
        if (!string.IsNullOrWhiteSpace(p.CompanyOverview)) sb.AppendLine($"Company / mission: {p.CompanyOverview}");
        if (!string.IsNullOrWhiteSpace(p.Website))       sb.AppendLine($"Website: {p.Website}");
        if (!string.IsNullOrWhiteSpace(p.Email))         sb.AppendLine($"Email: {p.Email}");
        if (!string.IsNullOrWhiteSpace(p.Phone))         sb.AppendLine($"Phone: {p.Phone}");
        if (!string.IsNullOrWhiteSpace(p.Location))      sb.AppendLine($"Location: {p.Location}");

        var content = sb.ToString().Trim();
        // Sanity floor: if FB returned literally nothing useful (just the page name),
        // skip the doc — wastes RAG storage and gives the LLM a useless context block.
        if (content.Length < 50)
        {
            return null;
        }
        return (content, ComputeFingerprint(content));
    }

    private static string? SelectVideoUrl(SocialPlatformPostSummaryResponse post)
    {
        // Posts whose MediaType is text/image are skipped — VideoRAG is heavy.
        if (string.IsNullOrWhiteSpace(post.MediaType))
        {
            return null;
        }
        var mt = post.MediaType.ToLowerInvariant();
        var looksLikeVideo = mt.Contains("video", StringComparison.Ordinal)
            || mt.Contains("reel", StringComparison.Ordinal);
        if (!looksLikeVideo)
        {
            return null;
        }

        // Prefer direct platform API video sources for Page-owned Facebook reels/videos.
        // Viewer URLs stay as a fallback for platforms where only the public URL exists.
        var videoUrl = FirstNonEmpty(post.VideoDownloadUrl, post.MediaUrl);
        if (!Uri.TryCreate(videoUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }
        return uri.Scheme is "http" or "https" ? videoUrl : null;
    }

    private static IndexSocialAccountPostReadItem BuildProfileReadItem()
    {
        return new IndexSocialAccountPostReadItem(
            PlatformPostId: "profile",
            Title: "Page profile",
            TextPreview: "About, category, website, contact, and location context.",
            MediaType: "profile",
            Permalink: null,
            PublishedAt: null,
            DocumentKinds: new[] { "profile" });
    }

    private static IndexSocialAccountPostReadItem BuildPostReadItem(
        SocialPlatformPostSummaryResponse post,
        string documentKind)
    {
        var title = FirstNonEmpty(post.Title, PreviewText(post.Text, 90), $"Post {post.PlatformPostId}");
        var preview = FirstNonEmpty(PreviewText(post.Text), PreviewText(post.Description));

        return new IndexSocialAccountPostReadItem(
            PlatformPostId: post.PlatformPostId,
            Title: title,
            TextPreview: preview,
            MediaType: post.MediaType,
            Permalink: FirstNonEmpty(post.Permalink, post.ShareUrl),
            PublishedAt: post.PublishedAt,
            DocumentKinds: new[] { documentKind });
    }

    private static IReadOnlyList<IndexSocialAccountPostReadItem> BuildReadBatchItems(
        IEnumerable<RagIngestMessage> batch,
        IReadOnlyDictionary<string, IndexSocialAccountPostReadItem> readItemsByDocumentId)
    {
        var posts = new Dictionary<string, IndexSocialAccountPostReadItem>(StringComparer.Ordinal);

        foreach (var doc in batch)
        {
            if (!readItemsByDocumentId.TryGetValue(doc.DocumentId, out var item))
            {
                continue;
            }

            if (posts.TryGetValue(item.PlatformPostId, out var existing))
            {
                var documentKinds = existing.DocumentKinds
                    .Concat(item.DocumentKinds)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                posts[item.PlatformPostId] = existing with { DocumentKinds = documentKinds };
                continue;
            }

            posts[item.PlatformPostId] = item;
        }

        return posts.Values.ToArray();
    }

    private static string? PreviewText(string? value, int max = 220)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var compact = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= max ? compact : compact[..max] + "...";
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static bool LooksLikeProviderCreditFailure(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        return error.Contains("HTTP 402", StringComparison.OrdinalIgnoreCase)
               || error.Contains("Insufficient credits", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPrefix(string platform, Guid socialMediaId)
        => $"{platform.ToLowerInvariant()}:{socialMediaId:N}:";

    private static (string Content, string Fingerprint) BuildTextDoc(
        string platform,
        SocialPlatformPostSummaryResponse post)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[Post {post.PlatformPostId} on {platform}]");

        if (!string.IsNullOrWhiteSpace(post.Title))
        {
            sb.AppendLine($"Title: {post.Title}");
        }
        if (!string.IsNullOrWhiteSpace(post.Text))
        {
            sb.AppendLine($"Caption: {post.Text}");
        }
        if (!string.IsNullOrWhiteSpace(post.Description))
        {
            sb.AppendLine($"Description: {post.Description}");
        }
        if (!string.IsNullOrWhiteSpace(post.MediaType))
        {
            sb.AppendLine($"MediaType: {post.MediaType}");
        }
        if (post.PublishedAt.HasValue)
        {
            sb.AppendLine($"PublishedAt: {post.PublishedAt.Value:O}");
        }
        if (!string.IsNullOrWhiteSpace(post.Permalink))
        {
            sb.AppendLine($"Permalink: {post.Permalink}");
        }

        var stats = post.Stats;
        if (stats != null)
        {
            var parts = new List<string>();
            if (stats.Views.HasValue) parts.Add($"{stats.Views} views");
            if (stats.Likes.HasValue) parts.Add($"{stats.Likes} likes");
            if (stats.Comments.HasValue) parts.Add($"{stats.Comments} comments");
            if (stats.Shares.HasValue) parts.Add($"{stats.Shares} shares");
            if (stats.Reposts.HasValue) parts.Add($"{stats.Reposts} reposts");
            if (stats.Quotes.HasValue) parts.Add($"{stats.Quotes} quotes");
            if (stats.Saves.HasValue) parts.Add($"{stats.Saves} saves");
            if (stats.Reach.HasValue) parts.Add($"{stats.Reach} reach");
            if (stats.Impressions.HasValue) parts.Add($"{stats.Impressions} impressions");

            sb.AppendLine(parts.Count > 0
                ? "Engagement: " + string.Join(" • ", parts)
                : "Engagement: n/a");

            sb.AppendLine($"TotalInteractions: {stats.TotalInteractions}");
        }

        var content = sb.ToString();
        return (content, ComputeFingerprint(content));
    }

    private static string? SelectImageUrl(SocialPlatformPostSummaryResponse post)
    {
        // ThumbnailUrl is consistently the direct CDN image URL across FB/IG/TikTok/Threads.
        // MediaUrl on Facebook is often the photo permalink page (facebook.com/photo.php?...),
        // which the vision provider tries to download as HTML and rejects with "unsupported format".
        var candidates = new[] { post.ThumbnailUrl, post.MediaUrl };
        foreach (var candidate in candidates)
        {
            if (LooksLikeDirectImageUrl(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private static bool LooksLikeDirectImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }
        if (uri.Scheme is not "http" and not "https")
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        // Known CDNs that always serve direct image bytes.
        var knownImageHosts = new[]
        {
            "fbcdn.net", "cdninstagram.com", "instagram.com",
            "tiktokcdn.com", "tiktokcdn-us.com",
            "threadscdn.net",
            "amazonaws.com", "cloudfront.net",
        };
        if (knownImageHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal)))
        {
            return true;
        }

        // HTML viewer pages on the platform itself (e.g. facebook.com/photo.php) are not images.
        var htmlViewerHosts = new[] { "facebook.com", "www.facebook.com", "m.facebook.com" };
        if (htmlViewerHosts.Contains(host))
        {
            return false;
        }

        // Fallback: trust paths that end with a known image extension.
        var path = uri.AbsolutePath.ToLowerInvariant();
        return path.EndsWith(".jpg", StringComparison.Ordinal)
               || path.EndsWith(".jpeg", StringComparison.Ordinal)
               || path.EndsWith(".png", StringComparison.Ordinal)
               || path.EndsWith(".gif", StringComparison.Ordinal)
               || path.EndsWith(".webp", StringComparison.Ordinal);
    }

    private static DocStatus Classify(
        IReadOnlyDictionary<string, string> existing,
        string documentId,
        string fingerprint)
    {
        if (!existing.TryGetValue(documentId, out var existingFingerprint))
        {
            return DocStatus.New;
        }
        return string.Equals(existingFingerprint, fingerprint, StringComparison.Ordinal)
            ? DocStatus.Unchanged
            : DocStatus.Updated;
    }

    private static string ComputeFingerprint(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private enum DocStatus
    {
        New,
        Updated,
        Unchanged,
    }
}
