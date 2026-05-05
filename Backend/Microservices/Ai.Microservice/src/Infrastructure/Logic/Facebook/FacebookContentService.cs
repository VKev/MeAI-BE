using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Facebook;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Facebook;

public sealed class FacebookContentService : IFacebookContentService
{
    private const string GraphApiBaseUrl = "https://graph.facebook.com/v25.0";
    private const string BasePostFields =
        "id,message,story,created_time,permalink_url,full_picture,shares,attachments{media_type,type,url,title,description,media,target{id},subattachments{media_type,type,url,title,description,media,target{id}}}";
    private const string PostInsightMetrics =
        "post_impressions_unique,post_reactions_by_type_total,post_activity_by_action_type";
    private const string PageInsightFields =
        "id,name,followers_count,fan_count," +
        "about,description,category,category_list," +
        "website,emails,phone,bio,founded,mission,company_overview," +
        "location{street,city,country,zip},single_line_address";
    private const string CommentFields = "id,message,created_time,like_count,comment_count,permalink_url,from{id,name}";

    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public FacebookContentService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("Facebook");
    }

    public async Task<Result<FacebookPostPageResult>> GetPostsAsync(
        FacebookPostListRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserAccessToken))
        {
            return Result.Failure<FacebookPostPageResult>(
                new Error("Facebook.InvalidToken", "Facebook user access token is missing."));
        }

        var pagesResult = await ResolvePagesAsync(
            request.UserAccessToken,
            request.PreferredPageId,
            request.PreferredPageAccessToken,
            cancellationToken);

        if (pagesResult.IsFailure)
        {
            return Result.Failure<FacebookPostPageResult>(pagesResult.Error);
        }

        var pages = pagesResult.Value;
        var limit = NormalizeLimit(request.Limit);
        var cursorState = DecodeCursor(request.Cursor);

        if (cursorState == null)
        {
            return Result.Failure<FacebookPostPageResult>(
                new Error("Facebook.InvalidCursor", "Facebook cursor is invalid."));
        }

        if (cursorState.PageIndex < 0 || cursorState.PageIndex >= pages.Count)
        {
            return Result.Success(new FacebookPostPageResult(Array.Empty<FacebookPostDetails>(), null, false));
        }

        var items = new List<FacebookPostDetails>(limit);
        var pageIndex = cursorState.PageIndex;
        var pageCursor = cursorState.PageCursor;

        while (pageIndex < pages.Count && items.Count < limit)
        {
            var page = pages[pageIndex];
            var remaining = limit - items.Count;
            var pagePostsResult = await FetchPagePostsAsync(
                page.PageId,
                page.PageAccessToken,
                remaining,
                pageCursor,
                cancellationToken);

            if (pagePostsResult.IsFailure)
            {
                return Result.Failure<FacebookPostPageResult>(pagePostsResult.Error);
            }

            items.AddRange(pagePostsResult.Value.Posts);

            if (pagePostsResult.Value.HasMore)
            {
                return Result.Success(new FacebookPostPageResult(
                    Posts: items,
                    NextCursor: EncodeCursor(new FacebookCursorState(pageIndex, pagePostsResult.Value.NextCursor)),
                    HasMore: true));
            }

            pageIndex++;
            pageCursor = null;

            if (items.Count >= limit && pageIndex < pages.Count)
            {
                return Result.Success(new FacebookPostPageResult(
                    Posts: items,
                    NextCursor: EncodeCursor(new FacebookCursorState(pageIndex, null)),
                    HasMore: true));
            }
        }

        return Result.Success(new FacebookPostPageResult(
            Posts: items,
            NextCursor: null,
            HasMore: false));
    }

    public async Task<Result<FacebookPostDetails>> GetPostAsync(
        FacebookPostDetailsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserAccessToken))
        {
            return Result.Failure<FacebookPostDetails>(
                new Error("Facebook.InvalidToken", "Facebook user access token is missing."));
        }

        if (string.IsNullOrWhiteSpace(request.PostId))
        {
            return Result.Failure<FacebookPostDetails>(
                new Error("Facebook.InvalidPostId", "Facebook post id is required."));
        }

        var pagesResult = await ResolvePagesAsync(
            request.UserAccessToken,
            request.PreferredPageId,
            request.PreferredPageAccessToken,
            cancellationToken);

        if (pagesResult.IsFailure)
        {
            return Result.Failure<FacebookPostDetails>(pagesResult.Error);
        }

        var pages = OrderPagesForLookup(pagesResult.Value, request.PostId);
        Error? lastError = null;

        foreach (var page in pages)
        {
            var postResult = await FetchPostForPageAsync(
                page.PageId,
                page.PageAccessToken,
                request.PostId,
                cancellationToken);

            if (postResult.IsSuccess)
            {
                return postResult;
            }

            lastError = postResult.Error;
        }

        return Result.Failure<FacebookPostDetails>(
            lastError ?? new Error("Facebook.PostNotFound", "Facebook post was not found for the current account."));
    }

    public async Task<Result<FacebookPageInsights>> GetPageInsightsAsync(
        FacebookPageInsightsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserAccessToken))
        {
            return Result.Failure<FacebookPageInsights>(
                new Error("Facebook.InvalidToken", "Facebook user access token is missing."));
        }

        var pagesResult = await ResolvePagesAsync(
            request.UserAccessToken,
            request.PreferredPageId,
            request.PreferredPageAccessToken,
            cancellationToken);

        if (pagesResult.IsFailure)
        {
            return Result.Failure<FacebookPageInsights>(pagesResult.Error);
        }

        foreach (var page in pagesResult.Value)
        {
            var url =
                $"{GraphApiBaseUrl}/{Uri.EscapeDataString(page.PageId)}?fields={Uri.EscapeDataString(PageInsightFields)}&access_token={Uri.EscapeDataString(page.PageAccessToken)}";

            var response = await SendGetAsync<FacebookPageInsightsDto>(url, cancellationToken);
            if (response.IsFailure)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(response.Value.Id))
            {
                continue;
            }

            // Pull category list (FB returns either a single `category` string or
            // a structured `category_list` array with name+id; combine both for
            // a clean comma-joined display).
            string? categoryDisplay = null;
            var nestedCategories = response.Value.CategoryList?
                .Select(c => c.Name?.Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Cast<string>()
                .ToList();
            if (nestedCategories is { Count: > 0 })
            {
                categoryDisplay = string.Join(", ", nestedCategories);
            }
            else if (!string.IsNullOrWhiteSpace(response.Value.Category))
            {
                categoryDisplay = response.Value.Category;
            }

            string? emailJoined = null;
            if (response.Value.Emails is { Length: > 0 })
            {
                emailJoined = string.Join(", ", response.Value.Emails.Where(e => !string.IsNullOrWhiteSpace(e)));
            }

            string? locationDisplay = response.Value.SingleLineAddress;
            if (string.IsNullOrWhiteSpace(locationDisplay) && response.Value.Location is not null)
            {
                var parts = new[]
                {
                    response.Value.Location.Street,
                    response.Value.Location.City,
                    response.Value.Location.Zip,
                    response.Value.Location.Country,
                }.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
                if (parts.Count > 0) locationDisplay = string.Join(", ", parts);
            }

            // Combine longer narrative fields when present — gives the LLM more
            // context without requiring per-field handling downstream.
            var overviewParts = new[]
            {
                response.Value.Mission, response.Value.CompanyOverview, response.Value.Founded,
            }.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            var companyOverview = overviewParts.Count > 0 ? string.Join(" — ", overviewParts) : null;

            return Result.Success(new FacebookPageInsights(
                PageId: response.Value.Id!,
                Name: response.Value.Name,
                Followers: response.Value.FollowersCount,
                Fans: response.Value.FanCount,
                About: response.Value.About,
                Description: response.Value.Description,
                Category: categoryDisplay,
                Website: response.Value.Website,
                Email: emailJoined,
                Phone: response.Value.Phone,
                Location: locationDisplay,
                Bio: response.Value.Bio,
                CompanyOverview: companyOverview));
        }

        return Result.Failure<FacebookPageInsights>(
            new Error("Facebook.ApiWarning", "Facebook page insights are unavailable."));
    }

    public async Task<Result<IReadOnlyList<SocialPlatformCommentItem>>> GetPostCommentsAsync(
        FacebookPostCommentsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserAccessToken) || string.IsNullOrWhiteSpace(request.PostId))
        {
            return Result.Failure<IReadOnlyList<SocialPlatformCommentItem>>(
                new Error("Facebook.InvalidRequest", "Facebook access token and post id are required."));
        }

        var pagesResult = await ResolvePagesAsync(
            request.UserAccessToken,
            request.PreferredPageId,
            request.PreferredPageAccessToken,
            cancellationToken);

        if (pagesResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<SocialPlatformCommentItem>>(pagesResult.Error);
        }

        var pages = OrderPagesForLookup(pagesResult.Value, request.PostId);
        var limit = NormalizeCommentLimit(request.Limit);

        foreach (var page in pages)
        {
            var url =
                $"{GraphApiBaseUrl}/{Uri.EscapeDataString(request.PostId)}/comments?fields={Uri.EscapeDataString(CommentFields)}&filter=stream&limit={limit}&access_token={Uri.EscapeDataString(page.PageAccessToken)}";

            var response = await SendGetAsync<FacebookCommentsApiResponse>(url, cancellationToken);
            if (response.IsFailure)
            {
                continue;
            }

            var comments = (response.Value.Data ?? Array.Empty<FacebookCommentDto>())
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .Select(item => new SocialPlatformCommentItem(
                    Id: item.Id!,
                    Text: item.Message,
                    AuthorId: item.From?.Id,
                    AuthorName: item.From?.Name,
                    AuthorUsername: null,
                    CreatedAt: ToDateTimeOffset(item.CreatedTime),
                    LikeCount: item.LikeCount,
                    ReplyCount: item.CommentCount,
                    Permalink: item.PermalinkUrl))
                .ToList();

            return Result.Success<IReadOnlyList<SocialPlatformCommentItem>>(comments);
        }

        return Result.Success<IReadOnlyList<SocialPlatformCommentItem>>(Array.Empty<SocialPlatformCommentItem>());
    }

    private async Task<Result<IReadOnlyList<PageAccessInfo>>> ResolvePagesAsync(
        string userAccessToken,
        string? preferredPageId,
        string? preferredPageAccessToken,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(preferredPageId) && !string.IsNullOrWhiteSpace(preferredPageAccessToken))
        {
            return Result.Success<IReadOnlyList<PageAccessInfo>>(
                new[] { new PageAccessInfo(preferredPageId, preferredPageAccessToken) });
        }

        var pagesResult = await FetchPagesAsync(userAccessToken, cancellationToken);
        if (pagesResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<PageAccessInfo>>(pagesResult.Error);
        }

        if (string.IsNullOrWhiteSpace(preferredPageId))
        {
            return pagesResult;
        }

        var page = pagesResult.Value.FirstOrDefault(item =>
            string.Equals(item.PageId, preferredPageId, StringComparison.Ordinal));

        if (page == null)
        {
            return Result.Failure<IReadOnlyList<PageAccessInfo>>(
                new Error("Facebook.PageNotFound", "The configured Facebook page was not found for this account."));
        }

        return Result.Success<IReadOnlyList<PageAccessInfo>>(new[] { page });
    }

    private async Task<Result<IReadOnlyList<PageAccessInfo>>> FetchPagesAsync(
        string userAccessToken,
        CancellationToken cancellationToken)
    {
        var url = $"{GraphApiBaseUrl}/me/accounts?fields=id,name,access_token&access_token={Uri.EscapeDataString(userAccessToken)}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<IReadOnlyList<PageAccessInfo>>(
                new Error("Facebook.PageLookupFailed", ReadGraphApiError(body) ?? "Failed to load Facebook pages."));
        }

        FacebookPagesResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<FacebookPagesResponse>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            return Result.Failure<IReadOnlyList<PageAccessInfo>>(
                new Error("Facebook.ParseError", $"Failed to parse Facebook page response: {ex.Message}"));
        }

        var pages = parsed?.Data
            ?.Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.AccessToken))
            .Select(item => new PageAccessInfo(item.Id!, item.AccessToken!))
            .ToList() ?? new List<PageAccessInfo>();

        if (pages.Count == 0)
        {
            return Result.Failure<IReadOnlyList<PageAccessInfo>>(
                new Error("Facebook.PageNotFound", "No Facebook pages were found for this account."));
        }

        return Result.Success<IReadOnlyList<PageAccessInfo>>(pages);
    }

    private async Task<Result<FacebookPostPageResult>> FetchPagePostsAsync(
        string pageId,
        string pageAccessToken,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var query = new List<string>
        {
            $"fields={Uri.EscapeDataString(BasePostFields)}",
            $"limit={limit}",
            $"access_token={Uri.EscapeDataString(pageAccessToken)}"
        };

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            query.Add($"after={Uri.EscapeDataString(cursor)}");
        }

        var url = $"{GraphApiBaseUrl}/{Uri.EscapeDataString(pageId)}/published_posts?{string.Join("&", query)}";
        var response = await SendGetAsync<FacebookPostsApiResponse>(url, cancellationToken);
        if (response.IsFailure)
        {
            return Result.Failure<FacebookPostPageResult>(response.Error);
        }

        // Note: we used to enrich video posts with `?fields=source` here so MediaUrl
        // pointed at the direct mp4. That was abandoned because FB serves reels as
        // DASH (video-only stream); the rag-microservice now uses yt-dlp on the
        // viewer URL (`facebook.com/reel/...`) which resolves DASH + audio properly.
        var posts = (response.Value.Data ?? Array.Empty<FacebookPostDto>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .Select(item => MapPost(pageId, item))
            .ToList();

        var nextCursor = response.Value.Paging?.Cursors?.After;
        var hasMore = !string.IsNullOrWhiteSpace(nextCursor);

        return Result.Success(new FacebookPostPageResult(
            Posts: posts,
            NextCursor: nextCursor,
            HasMore: hasMore));
    }

    private async Task<Result<FacebookPostDetails>> FetchPostForPageAsync(
        string pageId,
        string pageAccessToken,
        string postId,
        CancellationToken cancellationToken)
    {
        var url =
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(postId)}?fields={Uri.EscapeDataString(BasePostFields)}&access_token={Uri.EscapeDataString(pageAccessToken)}";

        var response = await SendGetAsync<FacebookPostDto>(url, cancellationToken, notFoundErrorCode: "Facebook.PostNotFound");
        if (response.IsFailure)
        {
            return Result.Failure<FacebookPostDetails>(response.Error);
        }

        if (string.IsNullOrWhiteSpace(response.Value.Id))
        {
            return Result.Failure<FacebookPostDetails>(
                new Error("Facebook.PostNotFound", "Facebook post was not found for the current account."));
        }

        var optionalMetricsTask = TryGetOptionalPostMetricsAsync(response.Value.Id, pageAccessToken, cancellationToken);
        var videoViewCountTask = TryGetVideoViewCountAsync(response.Value, pageAccessToken, cancellationToken);

        await Task.WhenAll(optionalMetricsTask, videoViewCountTask);

        var optionalMetrics = await optionalMetricsTask;
        var viewCount = await videoViewCountTask;

        return Result.Success(MapPost(
            pageId,
            response.Value,
            viewCount,
            optionalMetrics.ReactionCount,
            optionalMetrics.CommentCount,
            response.Value.Shares?.Count ?? optionalMetrics.ShareCount,
            optionalMetrics.ReactionBreakdown,
            optionalMetrics.ReachCount,
            optionalMetrics.ImpressionCount));
    }

    private async Task<FacebookOptionalMetrics> TryGetOptionalPostMetricsAsync(
        string postId,
        string pageAccessToken,
        CancellationToken cancellationToken)
    {
        var insightsTask = TryGetPostInsightsAsync(postId, pageAccessToken, cancellationToken);
        var reactionCountTask = TryGetReactionCountAsync(postId, pageAccessToken, cancellationToken);
        var commentCountTask = TryGetCommentCountAsync(postId, pageAccessToken, cancellationToken);

        await Task.WhenAll(insightsTask, reactionCountTask, commentCountTask);

        var insights = await insightsTask;

        return new FacebookOptionalMetrics(
            ReachCount: insights?.ReachCount,
            ImpressionCount: insights?.ImpressionCount,
            ReactionCount: await reactionCountTask ?? insights?.ReactionCount,
            CommentCount: await commentCountTask ?? insights?.CommentCount,
            ShareCount: insights?.ShareCount,
            ReactionBreakdown: insights?.ReactionBreakdown);
    }

    private async Task<long?> TryGetCommentCountAsync(
        string postId,
        string pageAccessToken,
        CancellationToken cancellationToken)
    {
        var url =
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(postId)}" +
            $"/comments?summary=true&filter=stream&limit=0&access_token={Uri.EscapeDataString(pageAccessToken)}";

        return await TryGetEdgeSummaryCountAsync(url, cancellationToken);
    }

    private async Task<long?> TryGetReactionCountAsync(
        string postId,
        string pageAccessToken,
        CancellationToken cancellationToken)
    {
        var url =
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(postId)}" +
            $"/reactions?summary=true&limit=0&access_token={Uri.EscapeDataString(pageAccessToken)}";

        return await TryGetEdgeSummaryCountAsync(url, cancellationToken);
    }

    private async Task<long?> TryGetEdgeSummaryCountAsync(
        string url,
        CancellationToken cancellationToken)
    {
        var response = await SendGetAsync<FacebookEdgeSummaryResponse>(url, cancellationToken);
        if (response.IsFailure)
        {
            return null;
        }

        return response.Value.Summary?.TotalCount;
    }

    private async Task<FacebookPostInsightMetrics?> TryGetPostInsightsAsync(
        string postId,
        string pageAccessToken,
        CancellationToken cancellationToken)
    {
        var url =
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(postId)}" +
            $"/insights?metric={Uri.EscapeDataString(PostInsightMetrics)}&access_token={Uri.EscapeDataString(pageAccessToken)}";

        var response = await SendGetAsync<FacebookPostInsightsResponse>(url, cancellationToken);
        if (response.IsFailure)
        {
            return null;
        }

        var reachCount = TryReadInsightMetricLong(response.Value, "post_impressions_unique");
        var reactionCount = TryReadInsightMetricTotal(response.Value, "post_reactions_by_type_total");
        var reactionBreakdown = TryReadInsightMetricBreakdown(response.Value, "post_reactions_by_type_total");
        var commentCount = TryReadInsightMetricActionCount(response.Value, "post_activity_by_action_type", "comment");
        var shareCount = TryReadInsightMetricActionCount(response.Value, "post_activity_by_action_type", "share");

        if (reachCount is null && reactionCount is null && commentCount is null && shareCount is null &&
            reactionBreakdown == null)
        {
            return null;
        }

        return new FacebookPostInsightMetrics(
            ReachCount: reachCount,
            ImpressionCount: null,
            ReactionCount: reactionCount,
            CommentCount: commentCount,
            ShareCount: shareCount,
            ReactionBreakdown: reactionBreakdown);
    }

    private async Task<long?> TryGetVideoViewCountAsync(
        FacebookPostDto post,
        string pageAccessToken,
        CancellationToken cancellationToken)
    {
        var attachment = post.Attachments?.Data?.FirstOrDefault();
        var nestedAttachment = attachment?.Subattachments?.Data?.FirstOrDefault();
        var effectiveAttachment = nestedAttachment ?? attachment;

        var mediaType = NormalizeMediaType(
            effectiveAttachment?.MediaType
            ?? attachment?.MediaType
            ?? effectiveAttachment?.Type
            ?? attachment?.Type);

        if (!string.Equals(mediaType, "video", StringComparison.Ordinal))
        {
            return null;
        }

        var videoId = TryGetVideoId(post);
        if (string.IsNullOrWhiteSpace(videoId) || string.IsNullOrWhiteSpace(pageAccessToken))
        {
            return null;
        }

        var url =
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(videoId)}" +
            $"/video_insights?metric=total_video_views&access_token={Uri.EscapeDataString(pageAccessToken)}";

        var response = await SendGetAsync<FacebookVideoInsightsResponse>(url, cancellationToken);
        if (response.IsFailure)
        {
            return null;
        }

        return TryReadInsightMetricValue(response.Value, "total_video_views");
    }

    private async Task<Result<TResponse>> SendGetAsync<TResponse>(
        string url,
        CancellationToken cancellationToken,
        string? notFoundErrorCode = null)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var message = ReadGraphApiError(body) ?? $"Facebook API request failed with status code {(int)response.StatusCode}.";
                var code = response.StatusCode == System.Net.HttpStatusCode.NotFound && !string.IsNullOrWhiteSpace(notFoundErrorCode)
                    ? notFoundErrorCode
                    : "Facebook.ApiError";

                return Result.Failure<TResponse>(new Error(code, message));
            }

            var parsed = JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
            if (parsed == null)
            {
                return Result.Failure<TResponse>(
                    new Error("Facebook.ParseError", "Failed to parse Facebook API response."));
            }

            return Result.Success(parsed);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<TResponse>(
                new Error("Facebook.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<TResponse>(
                new Error("Facebook.ParseError", $"JSON parse error: {ex.Message}"));
        }
    }

    private static IReadOnlyList<PageAccessInfo> OrderPagesForLookup(
        IReadOnlyList<PageAccessInfo> pages,
        string postId)
    {
        var ownerPageId = TryExtractPageId(postId);
        if (string.IsNullOrWhiteSpace(ownerPageId))
        {
            return pages;
        }

        return pages
            .OrderByDescending(page => string.Equals(page.PageId, ownerPageId, StringComparison.Ordinal))
            .ToList();
    }

    private static FacebookPostDetails MapPost(
        string pageId,
        FacebookPostDto post,
        long? viewCount = null,
        long? reactionCount = null,
        long? commentCount = null,
        long? shareCount = null,
        IReadOnlyDictionary<string, long>? reactionBreakdown = null,
        long? reachCount = null,
        long? impressionCount = null)
    {
        var attachment = post.Attachments?.Data?.FirstOrDefault();
        var nestedAttachment = attachment?.Subattachments?.Data?.FirstOrDefault();
        var effectiveAttachment = nestedAttachment ?? attachment;

        var mediaImageUrl = effectiveAttachment?.Media?.Image?.Src
                            ?? attachment?.Media?.Image?.Src;

        var mediaType = NormalizeMediaType(
            effectiveAttachment?.MediaType
            ?? attachment?.MediaType
            ?? effectiveAttachment?.Type
            ?? attachment?.Type);
        var mediaUrl = effectiveAttachment?.Url
                       ?? attachment?.Url
                       ?? mediaImageUrl
                       ?? post.FullPicture;

        var thumbnailUrl = mediaImageUrl
                           ?? post.FullPicture;

        return new FacebookPostDetails(
            Id: post.Id ?? string.Empty,
            PageId: pageId,
            Message: post.Message,
            Story: post.Story,
            PermalinkUrl: post.PermalinkUrl,
            CreatedTime: post.CreatedTime,
            FullPictureUrl: post.FullPicture,
            MediaType: mediaType,
            MediaUrl: mediaUrl,
            ThumbnailUrl: thumbnailUrl,
            AttachmentTitle: effectiveAttachment?.Title ?? attachment?.Title,
            AttachmentDescription: effectiveAttachment?.Description ?? attachment?.Description,
            ViewCount: viewCount,
            ReactionCount: reactionCount ?? post.Reactions?.Summary?.TotalCount,
            CommentCount: commentCount ?? post.Comments?.Summary?.TotalCount,
            ShareCount: shareCount ?? post.Shares?.Count ?? 0,
            ReactionBreakdown: reactionBreakdown,
            ReachCount: reachCount,
            ImpressionCount: impressionCount);
    }

    private static string? NormalizeMediaType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.ToLowerInvariant();

        return normalized switch
        {
            "photo" => "image",
            "album" => "image",
            "video_inline" => "video",
            _ when normalized.Contains("video", StringComparison.Ordinal) => "video",
            _ when normalized.Contains("photo", StringComparison.Ordinal) => "image",
            _ when normalized.Contains("image", StringComparison.Ordinal) => "image",
            _ => normalized
        };
    }

    private static int NormalizeLimit(int? limit)
    {
        if (limit is null or <= 0)
        {
            return 20;
        }

        return Math.Min(limit.Value, 50);
    }

    private static int NormalizeCommentLimit(int? limit)
    {
        if (limit is null or <= 0)
        {
            return 25;
        }

        return Math.Min(limit.Value, 100);
    }

    private static DateTimeOffset? ToDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : null;
    }

    private static FacebookCursorState? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return new FacebookCursorState(0, null);
        }

        try
        {
            var bytes = Convert.FromBase64String(cursor);
            var json = Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<FacebookCursorState>(json, JsonOptions);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string EncodeCursor(FacebookCursorState cursor)
    {
        var json = JsonSerializer.Serialize(cursor, JsonOptions);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static string? TryExtractPageId(string postId)
    {
        var separatorIndex = postId.IndexOf('_', StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            return null;
        }

        return postId[..separatorIndex];
    }

    private static string? TryGetVideoId(FacebookPostDto post)
    {
        if (!string.IsNullOrWhiteSpace(post.ObjectId))
        {
            return post.ObjectId;
        }

        var attachment = post.Attachments?.Data?.FirstOrDefault();
        var nestedAttachment = attachment?.Subattachments?.Data?.FirstOrDefault();

        return nestedAttachment?.Target?.Id
               ?? attachment?.Target?.Id;
    }

    private static long? TryReadInsightMetricValue(FacebookVideoInsightsResponse response, string metricName)
    {
        var metric = response.Data?.FirstOrDefault(item =>
            string.Equals(item.Name, metricName, StringComparison.Ordinal));

        return metric?.Values?.FirstOrDefault()?.Value;
    }

    private static long? TryReadInsightMetricLong(FacebookPostInsightsResponse response, string metricName)
    {
        var value = TryGetInsightMetricValue(response, metricName);
        return value is null ? null : TryReadLong(value.Value);
    }

    private static long? TryReadInsightMetricTotal(FacebookPostInsightsResponse response, string metricName)
    {
        var breakdown = TryReadInsightMetricBreakdown(response, metricName);
        if (breakdown == null)
        {
            return null;
        }

        return breakdown.Count == 0 ? null : breakdown.Values.Sum();
    }

    private static IReadOnlyDictionary<string, long>? TryReadInsightMetricBreakdown(
        FacebookPostInsightsResponse response,
        string metricName)
    {
        var value = TryGetInsightMetricValue(response, metricName);
        if (value is null || value.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in value.Value.EnumerateObject())
        {
            var metricValue = TryReadLong(property.Value);
            if (metricValue is null)
            {
                continue;
            }

            result[property.Name] = metricValue.Value;
        }

        return result.Count == 0 ? null : result;
    }

    private static long? TryReadInsightMetricActionCount(
        FacebookPostInsightsResponse response,
        string metricName,
        string actionName)
    {
        var value = TryGetInsightMetricValue(response, metricName);
        if (value is null || value.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in value.Value.EnumerateObject())
        {
            if (!property.Name.Contains(actionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return TryReadLong(property.Value);
        }

        return null;
    }

    private static JsonElement? TryGetInsightMetricValue(FacebookPostInsightsResponse response, string metricName)
    {
        var metric = response.Data?.FirstOrDefault(item =>
            string.Equals(item.Name, metricName, StringComparison.Ordinal));

        var value = metric?.Values?.FirstOrDefault()?.Value;
        return value is null ? null : value.Value.Clone();
    }

    private static long? TryReadLong(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var numericValue))
        {
            return numericValue;
        }

        if (element.ValueKind == JsonValueKind.String && long.TryParse(element.GetString(), out var stringValue))
        {
            return stringValue;
        }

        return null;
    }

    private static string? ReadGraphApiError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var error = JsonSerializer.Deserialize<GraphApiErrorResponse>(payload, JsonOptions);
            return error?.Error?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record FacebookCursorState(int PageIndex, string? PageCursor);

    private sealed record PageAccessInfo(string PageId, string PageAccessToken);

    private sealed record FacebookOptionalMetrics(
        long? ReachCount,
        long? ImpressionCount,
        long? ReactionCount,
        long? CommentCount,
        long? ShareCount,
        IReadOnlyDictionary<string, long>? ReactionBreakdown);

    private sealed record FacebookPostInsightMetrics(
        long? ReachCount,
        long? ImpressionCount,
        long? ReactionCount,
        long? CommentCount,
        long? ShareCount,
        IReadOnlyDictionary<string, long>? ReactionBreakdown);

    private sealed class FacebookPostsApiResponse
    {
        [JsonPropertyName("data")]
        public FacebookPostDto[]? Data { get; set; }

        [JsonPropertyName("paging")]
        public FacebookPagingDto? Paging { get; set; }
    }

    private sealed class FacebookPagingDto
    {
        [JsonPropertyName("cursors")]
        public FacebookPagingCursorDto? Cursors { get; set; }
    }

    private sealed class FacebookPagingCursorDto
    {
        [JsonPropertyName("after")]
        public string? After { get; set; }
    }

    private sealed class FacebookPagesResponse
    {
        [JsonPropertyName("data")]
        public FacebookPageDto[]? Data { get; set; }
    }

    private sealed class FacebookPageDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }

    private sealed class FacebookPageInsightsDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("followers_count")] public long? FollowersCount { get; set; }
        [JsonPropertyName("fan_count")] public long? FanCount { get; set; }

        // Profile / introduction fields
        [JsonPropertyName("about")] public string? About { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("category")] public string? Category { get; set; }
        [JsonPropertyName("category_list")] public FacebookCategoryDto[]? CategoryList { get; set; }
        [JsonPropertyName("website")] public string? Website { get; set; }
        [JsonPropertyName("emails")] public string[]? Emails { get; set; }
        [JsonPropertyName("phone")] public string? Phone { get; set; }
        [JsonPropertyName("bio")] public string? Bio { get; set; }
        [JsonPropertyName("founded")] public string? Founded { get; set; }
        [JsonPropertyName("mission")] public string? Mission { get; set; }
        [JsonPropertyName("company_overview")] public string? CompanyOverview { get; set; }
        [JsonPropertyName("location")] public FacebookLocationDto? Location { get; set; }
        [JsonPropertyName("single_line_address")] public string? SingleLineAddress { get; set; }
    }

    private sealed class FacebookCategoryDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class FacebookLocationDto
    {
        [JsonPropertyName("street")] public string? Street { get; set; }
        [JsonPropertyName("city")] public string? City { get; set; }
        [JsonPropertyName("country")] public string? Country { get; set; }
        [JsonPropertyName("zip")] public string? Zip { get; set; }
    }

    private sealed class FacebookPostDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("story")]
        public string? Story { get; set; }

        [JsonPropertyName("created_time")]
        public string? CreatedTime { get; set; }

        [JsonPropertyName("permalink_url")]
        public string? PermalinkUrl { get; set; }

        [JsonPropertyName("full_picture")]
        public string? FullPicture { get; set; }

        [JsonPropertyName("object_id")]
        public string? ObjectId { get; set; }

        [JsonPropertyName("shares")]
        public FacebookShareDto? Shares { get; set; }

        [JsonPropertyName("comments")]
        public FacebookSummaryContainerDto? Comments { get; set; }

        [JsonPropertyName("reactions")]
        public FacebookSummaryContainerDto? Reactions { get; set; }

        [JsonPropertyName("attachments")]
        public FacebookAttachmentConnectionDto? Attachments { get; set; }
    }

    private sealed class FacebookShareDto
    {
        [JsonPropertyName("count")]
        public long? Count { get; set; }
    }

    private sealed class FacebookSummaryContainerDto
    {
        [JsonPropertyName("summary")]
        public FacebookSummaryDto? Summary { get; set; }
    }

    private sealed class FacebookEdgeSummaryResponse
    {
        [JsonPropertyName("summary")]
        public FacebookSummaryDto? Summary { get; set; }
    }

    private sealed class FacebookCommentsApiResponse
    {
        [JsonPropertyName("data")]
        public FacebookCommentDto[]? Data { get; set; }
    }

    private sealed class FacebookCommentDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("created_time")]
        public string? CreatedTime { get; set; }

        [JsonPropertyName("like_count")]
        public long? LikeCount { get; set; }

        [JsonPropertyName("comment_count")]
        public long? CommentCount { get; set; }

        [JsonPropertyName("permalink_url")]
        public string? PermalinkUrl { get; set; }

        [JsonPropertyName("from")]
        public FacebookCommentAuthorDto? From { get; set; }
    }

    private sealed class FacebookCommentAuthorDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private sealed class FacebookSummaryDto
    {
        [JsonPropertyName("total_count")]
        public long? TotalCount { get; set; }
    }

    private sealed class FacebookAttachmentConnectionDto
    {
        [JsonPropertyName("data")]
        public FacebookAttachmentDto[]? Data { get; set; }
    }

    private sealed class FacebookAttachmentDto
    {
        [JsonPropertyName("media_type")]
        public string? MediaType { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("media")]
        public FacebookAttachmentMediaDto? Media { get; set; }

        [JsonPropertyName("target")]
        public FacebookAttachmentTargetDto? Target { get; set; }

        [JsonPropertyName("subattachments")]
        public FacebookAttachmentConnectionDto? Subattachments { get; set; }
    }

    private sealed class FacebookAttachmentMediaDto
    {
        [JsonPropertyName("image")]
        public FacebookAttachmentImageDto? Image { get; set; }
    }

    private sealed class FacebookAttachmentImageDto
    {
        [JsonPropertyName("src")]
        public string? Src { get; set; }
    }

    private sealed class FacebookAttachmentTargetDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    private sealed class FacebookVideoInsightsResponse
    {
        [JsonPropertyName("data")]
        public FacebookVideoInsightDto[]? Data { get; set; }
    }

    private sealed class FacebookVideoInsightDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("values")]
        public FacebookVideoInsightValueDto[]? Values { get; set; }
    }

    private sealed class FacebookVideoInsightValueDto
    {
        [JsonPropertyName("value")]
        public long? Value { get; set; }
    }

    private sealed class FacebookPostInsightsResponse
    {
        [JsonPropertyName("data")]
        public FacebookPostInsightDto[]? Data { get; set; }
    }

    private sealed class FacebookPostInsightDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("values")]
        public FacebookPostInsightValueDto[]? Values { get; set; }
    }

    private sealed class FacebookPostInsightValueDto
    {
        [JsonPropertyName("value")]
        public JsonElement? Value { get; set; }
    }

    private sealed class GraphApiErrorResponse
    {
        [JsonPropertyName("error")]
        public GraphApiError? Error { get; set; }
    }

    private sealed class GraphApiError
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
