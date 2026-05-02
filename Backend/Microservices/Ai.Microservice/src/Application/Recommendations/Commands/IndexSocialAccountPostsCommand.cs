using System.Security.Cryptography;
using System.Text;
using Application.Abstractions.Rag;
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
    int? MaxPosts = null) : IRequest<Result<IndexSocialAccountPostsResponse>>;

public sealed class IndexSocialAccountPostsCommandHandler
    : IRequestHandler<IndexSocialAccountPostsCommand, Result<IndexSocialAccountPostsResponse>>
{
    private const int PageSize = 25;
    private const int DefaultMaxPosts = 200;
    private const int HardCapPosts = 2000;

    private const string ImageDescribePrompt =
        "Describe this social media post image so it can be retrieved by semantic search and used " +
        "to recommend similar future content. Cover: subject, brand or logo, visible text/OCR, " +
        "color palette, mood, and visual style. Be concise but specific.";

    private readonly IMediator _mediator;
    private readonly IRagClient _ragClient;
    private readonly ILogger<IndexSocialAccountPostsCommandHandler> _logger;

    public IndexSocialAccountPostsCommandHandler(
        IMediator mediator,
        IRagClient ragClient,
        ILogger<IndexSocialAccountPostsCommandHandler> logger)
    {
        _mediator = mediator;
        _ragClient = ragClient;
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
                    queuedImage++;
                }
            }
        }

        if (docsToQueue.Count > 0)
        {
            await _ragClient.PublishIngestBatchAsync(docsToQueue, cancellationToken);
            _logger.LogInformation(
                "Queued {DocCount} RAG ingest documents for socialMediaId={SocialMediaId} (text={Text}, image={Image})",
                docsToQueue.Count,
                request.SocialMediaId,
                queuedText,
                queuedImage);
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
            QueuedImageDocuments: queuedImage));
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
