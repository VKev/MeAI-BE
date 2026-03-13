using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Threads;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Threads;

public sealed class ThreadsContentService : IThreadsContentService
{
    private const string GraphApiBaseUrl = "https://graph.threads.net/v1.0";
    private const string PostFields =
        "id,media_product_type,media_type,media_url,gif_url,permalink,username,text,timestamp,shortcode,thumbnail_url,is_quote_post,has_replies,alt_text,link_attachment_url,topic_tag,profile_picture_url";
    private const string InsightsMetrics = "views,likes,replies,reposts,quotes,shares";

    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public ThreadsContentService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("Threads");
    }

    public async Task<Result<ThreadsPostPageResult>> GetPostsAsync(
        ThreadsPostListRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Result.Failure<ThreadsPostPageResult>(
                new Error("Threads.InvalidToken", "Threads access token is missing."));
        }

        var query = new List<string>
        {
            $"fields={Uri.EscapeDataString(PostFields)}",
            $"access_token={Uri.EscapeDataString(request.AccessToken)}",
            $"limit={NormalizeLimit(request.Limit)}"
        };

        if (!string.IsNullOrWhiteSpace(request.Cursor))
        {
            query.Add($"after={Uri.EscapeDataString(request.Cursor)}");
        }

        var url = $"{GraphApiBaseUrl}/me/threads?{string.Join("&", query)}";
        var response = await SendGetAsync<ThreadsPostsApiResponse>(url, cancellationToken);
        if (response.IsFailure)
        {
            return Result.Failure<ThreadsPostPageResult>(response.Error);
        }

        var posts = (response.Value.Data ?? Array.Empty<ThreadsPostDto>())
            .Select(MapPost)
            .ToList();

        var nextCursor = response.Value.Paging?.Cursors?.After;
        var hasMore = !string.IsNullOrWhiteSpace(nextCursor) &&
                      (!string.IsNullOrWhiteSpace(response.Value.Paging?.Next) || posts.Count > 0);

        return Result.Success(new ThreadsPostPageResult(
            Posts: posts,
            NextCursor: nextCursor,
            HasMore: hasMore));
    }

    public async Task<Result<ThreadsPostDetails>> GetPostAsync(
        ThreadsPostDetailsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Result.Failure<ThreadsPostDetails>(
                new Error("Threads.InvalidToken", "Threads access token is missing."));
        }

        if (string.IsNullOrWhiteSpace(request.PostId))
        {
            return Result.Failure<ThreadsPostDetails>(
                new Error("Threads.InvalidPostId", "Threads post id is required."));
        }

        var url =
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(request.PostId)}?fields={Uri.EscapeDataString(PostFields)}&access_token={Uri.EscapeDataString(request.AccessToken)}";

        var response = await SendGetAsync<ThreadsPostDto>(url, cancellationToken);
        if (response.IsFailure)
        {
            return Result.Failure<ThreadsPostDetails>(response.Error);
        }

        if (string.IsNullOrWhiteSpace(response.Value.Id))
        {
            return Result.Failure<ThreadsPostDetails>(
                new Error("Threads.PostNotFound", "Threads post was not found for the current account."));
        }

        return Result.Success(MapPost(response.Value));
    }

    public async Task<Result<ThreadsPostInsights>> GetPostInsightsAsync(
        ThreadsPostInsightsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Result.Failure<ThreadsPostInsights>(
                new Error("Threads.InvalidToken", "Threads access token is missing."));
        }

        if (string.IsNullOrWhiteSpace(request.PostId))
        {
            return Result.Failure<ThreadsPostInsights>(
                new Error("Threads.InvalidPostId", "Threads post id is required."));
        }

        var url =
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(request.PostId)}/insights?metric={Uri.EscapeDataString(InsightsMetrics)}&access_token={Uri.EscapeDataString(request.AccessToken)}";

        var response = await SendGetAsync<ThreadsInsightsApiResponse>(url, cancellationToken);
        if (response.IsFailure)
        {
            return Result.Failure<ThreadsPostInsights>(response.Error);
        }

        var metrics = (response.Value.Data ?? Array.Empty<ThreadsInsightMetricDto>())
            .ToDictionary(
                item => item.Name ?? string.Empty,
                item => item.GetLongValue(),
                StringComparer.OrdinalIgnoreCase);

        return Result.Success(new ThreadsPostInsights(
            Views: GetMetric(metrics, "views"),
            Likes: GetMetric(metrics, "likes"),
            Replies: GetMetric(metrics, "replies"),
            Reposts: GetMetric(metrics, "reposts"),
            Quotes: GetMetric(metrics, "quotes"),
            Shares: GetMetric(metrics, "shares")));
    }

    private async Task<Result<TResponse>> SendGetAsync<TResponse>(
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<TResponse>(
                    new Error("Threads.ApiError", ReadGraphApiError(body) ?? $"Threads API request failed with status code {(int)response.StatusCode}."));
            }

            var parsed = JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
            if (parsed == null)
            {
                return Result.Failure<TResponse>(
                    new Error("Threads.ParseError", "Failed to parse Threads API response."));
            }

            return Result.Success(parsed);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<TResponse>(
                new Error("Threads.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<TResponse>(
                new Error("Threads.ParseError", $"JSON parse error: {ex.Message}"));
        }
    }

    private static int NormalizeLimit(int? limit)
    {
        if (limit is null or <= 0)
        {
            return 20;
        }

        return Math.Min(limit.Value, 50);
    }

    private static ThreadsPostDetails MapPost(ThreadsPostDto post)
    {
        return new ThreadsPostDetails(
            Id: post.Id ?? string.Empty,
            MediaProductType: post.MediaProductType,
            MediaType: post.MediaType,
            MediaUrl: post.MediaUrl,
            GifUrl: post.GifUrl,
            Permalink: post.Permalink,
            Username: post.Username,
            Text: post.Text,
            Timestamp: post.Timestamp,
            Shortcode: post.Shortcode,
            ThumbnailUrl: post.ThumbnailUrl,
            IsQuotePost: post.IsQuotePost,
            HasReplies: post.HasReplies,
            AltText: post.AltText,
            LinkAttachmentUrl: post.LinkAttachmentUrl,
            TopicTag: post.TopicTag,
            ProfilePictureUrl: post.ProfilePictureUrl);
    }

    private static long? GetMetric(IReadOnlyDictionary<string, long?> metrics, string name)
    {
        return metrics.TryGetValue(name, out var value) ? value : null;
    }

    private static string? ReadGraphApiError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var error = JsonSerializer.Deserialize<ThreadsGraphApiErrorResponse>(payload, JsonOptions);
            return error?.Error?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class ThreadsPostsApiResponse
    {
        [JsonPropertyName("data")]
        public ThreadsPostDto[]? Data { get; set; }

        [JsonPropertyName("paging")]
        public ThreadsPagingDto? Paging { get; set; }
    }

    private sealed class ThreadsPagingDto
    {
        [JsonPropertyName("cursors")]
        public ThreadsPagingCursorDto? Cursors { get; set; }

        [JsonPropertyName("next")]
        public string? Next { get; set; }
    }

    private sealed class ThreadsPagingCursorDto
    {
        [JsonPropertyName("after")]
        public string? After { get; set; }
    }

    private sealed class ThreadsPostDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("media_product_type")]
        public string? MediaProductType { get; set; }

        [JsonPropertyName("media_type")]
        public string? MediaType { get; set; }

        [JsonPropertyName("media_url")]
        public string? MediaUrl { get; set; }

        [JsonPropertyName("gif_url")]
        public string? GifUrl { get; set; }

        [JsonPropertyName("permalink")]
        public string? Permalink { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; set; }

        [JsonPropertyName("shortcode")]
        public string? Shortcode { get; set; }

        [JsonPropertyName("thumbnail_url")]
        public string? ThumbnailUrl { get; set; }

        [JsonPropertyName("is_quote_post")]
        public bool? IsQuotePost { get; set; }

        [JsonPropertyName("has_replies")]
        public bool? HasReplies { get; set; }

        [JsonPropertyName("alt_text")]
        public string? AltText { get; set; }

        [JsonPropertyName("link_attachment_url")]
        public string? LinkAttachmentUrl { get; set; }

        [JsonPropertyName("topic_tag")]
        public string? TopicTag { get; set; }

        [JsonPropertyName("profile_picture_url")]
        public string? ProfilePictureUrl { get; set; }
    }

    private sealed class ThreadsInsightsApiResponse
    {
        [JsonPropertyName("data")]
        public ThreadsInsightMetricDto[]? Data { get; set; }
    }

    private sealed class ThreadsInsightMetricDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("values")]
        public ThreadsInsightValueDto[]? Values { get; set; }

        public long? GetLongValue()
        {
            var element = Values?.FirstOrDefault()?.Value;
            if (element is null)
            {
                return null;
            }

            var value = element.Value;

            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt64(out var number) => number,
                JsonValueKind.String when long.TryParse(value.GetString(), out var number) => number,
                _ => null
            };
        }
    }

    private sealed class ThreadsInsightValueDto
    {
        [JsonPropertyName("value")]
        public JsonElement Value { get; set; }
    }

    private sealed class ThreadsGraphApiErrorResponse
    {
        [JsonPropertyName("error")]
        public ThreadsGraphApiError? Error { get; set; }
    }

    private sealed class ThreadsGraphApiError
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
