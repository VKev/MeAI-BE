using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Facebook;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Facebook;

public sealed class FacebookContentService : IFacebookContentService
{
    private const string GraphApiBaseUrl = "https://graph.facebook.com/v25.0";
    private const string PostFields =
        "id,message,story,created_time,permalink_url,full_picture,shares,comments.limit(0).summary(true),reactions.limit(0).summary(true),attachments{media_type,type,url,title,description,media,target,subattachments}";

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
            $"fields={Uri.EscapeDataString(PostFields)}",
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
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(postId)}?fields={Uri.EscapeDataString(PostFields)}&access_token={Uri.EscapeDataString(pageAccessToken)}";

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

        return Result.Success(MapPost(pageId, response.Value));
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

    private static FacebookPostDetails MapPost(string pageId, FacebookPostDto post)
    {
        var attachment = post.Attachments?.Data?.FirstOrDefault();
        var nestedAttachment = attachment?.Subattachments?.Data?.FirstOrDefault();
        var effectiveAttachment = nestedAttachment ?? attachment;

        var mediaImageUrl = effectiveAttachment?.Media?.Image?.Src
                            ?? attachment?.Media?.Image?.Src;

        var mediaType = NormalizeMediaType(effectiveAttachment?.MediaType ?? attachment?.MediaType ?? attachment?.Type);
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
            ReactionCount: post.Reactions?.Summary?.TotalCount,
            CommentCount: post.Comments?.Summary?.TotalCount,
            ShareCount: post.Shares?.Count);
    }

    private static string? NormalizeMediaType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.ToLowerInvariant() switch
        {
            "photo" => "image",
            "album" => "image",
            "video_inline" => "video",
            _ => value.ToLowerInvariant()
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
