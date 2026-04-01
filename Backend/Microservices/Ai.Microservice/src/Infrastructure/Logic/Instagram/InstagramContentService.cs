using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Instagram;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Instagram;

public sealed class InstagramContentService : IInstagramContentService
{
    private const string GraphApiBaseUrl = "https://graph.facebook.com/v24.0";
    private const string PostFields =
        "id,caption,media_type,media_product_type,media_url,thumbnail_url,permalink,timestamp,username,like_count,comments_count";
    private const string InsightMetrics = "views,reach,impressions,saved,shares";

    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public InstagramContentService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("Instagram");
    }

    public async Task<Result<InstagramPostPageResult>> GetPostsAsync(
        InstagramPostListRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Result.Failure<InstagramPostPageResult>(
                new Error("Instagram.InvalidToken", "Instagram access token is missing."));
        }

        if (string.IsNullOrWhiteSpace(request.InstagramUserId))
        {
            return Result.Failure<InstagramPostPageResult>(
                new Error("Instagram.InvalidAccount", "Instagram business account id is missing."));
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

        var url =
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(request.InstagramUserId)}/media?{string.Join("&", query)}";

        var response = await SendGetAsync<InstagramPostsApiResponse>(url, cancellationToken);
        if (response.IsFailure)
        {
            return Result.Failure<InstagramPostPageResult>(response.Error);
        }

        var posts = (response.Value.Data ?? Array.Empty<InstagramPostDto>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .Select(MapPost)
            .ToList();

        var nextCursor = response.Value.Paging?.Cursors?.After;
        var hasMore = !string.IsNullOrWhiteSpace(nextCursor);

        return Result.Success(new InstagramPostPageResult(
            Posts: posts,
            NextCursor: nextCursor,
            HasMore: hasMore));
    }

    public async Task<Result<InstagramPostDetails>> GetPostAsync(
        InstagramPostDetailsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Result.Failure<InstagramPostDetails>(
                new Error("Instagram.InvalidToken", "Instagram access token is missing."));
        }

        if (string.IsNullOrWhiteSpace(request.PostId))
        {
            return Result.Failure<InstagramPostDetails>(
                new Error("Instagram.InvalidPostId", "Instagram post id is required."));
        }

        var url =
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(request.PostId)}?fields={Uri.EscapeDataString(PostFields)}&access_token={Uri.EscapeDataString(request.AccessToken)}";

        var response = await SendGetAsync<InstagramPostDto>(url, cancellationToken);
        if (response.IsFailure)
        {
            return Result.Failure<InstagramPostDetails>(response.Error);
        }

        if (string.IsNullOrWhiteSpace(response.Value.Id))
        {
            return Result.Failure<InstagramPostDetails>(
                new Error("Instagram.PostNotFound", "Instagram post was not found for the current account."));
        }

        return Result.Success(MapPost(response.Value));
    }

    public async Task<Result<InstagramPostInsights>> GetPostInsightsAsync(
        InstagramPostInsightsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Result.Failure<InstagramPostInsights>(
                new Error("Instagram.InvalidToken", "Instagram access token is missing."));
        }

        if (string.IsNullOrWhiteSpace(request.PostId))
        {
            return Result.Failure<InstagramPostInsights>(
                new Error("Instagram.InvalidPostId", "Instagram post id is required."));
        }

        var url =
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(request.PostId)}/insights?metric={Uri.EscapeDataString(InsightMetrics)}&access_token={Uri.EscapeDataString(request.AccessToken)}";

        var response = await SendGetAsync<InstagramInsightsApiResponse>(url, cancellationToken, allowFailure: true);
        if (response.IsFailure)
        {
            return Result.Success(new InstagramPostInsights(
                Views: null,
                Reach: null,
                Impressions: null,
                Saved: null,
                Shares: null));
        }

        var metrics = (response.Value.Data ?? Array.Empty<InstagramInsightMetricDto>())
            .ToDictionary(
                item => item.Name ?? string.Empty,
                item => item.GetLongValue(),
                StringComparer.OrdinalIgnoreCase);

        return Result.Success(new InstagramPostInsights(
            Views: GetMetric(metrics, "views"),
            Reach: GetMetric(metrics, "reach"),
            Impressions: GetMetric(metrics, "impressions"),
            Saved: GetMetric(metrics, "saved"),
            Shares: GetMetric(metrics, "shares")));
    }

    private async Task<Result<TResponse>> SendGetAsync<TResponse>(
        string url,
        CancellationToken cancellationToken,
        bool allowFailure = false)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (allowFailure)
                {
                    return Result.Failure<TResponse>(new Error("Instagram.ApiWarning", "Optional Instagram endpoint is unavailable."));
                }

                return Result.Failure<TResponse>(
                    new Error("Instagram.ApiError", ReadGraphApiError(body) ?? $"Instagram API request failed with status code {(int)response.StatusCode}."));
            }

            var parsed = JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
            if (parsed == null)
            {
                return Result.Failure<TResponse>(
                    new Error("Instagram.ParseError", "Failed to parse Instagram API response."));
            }

            return Result.Success(parsed);
        }
        catch (HttpRequestException ex)
        {
            if (allowFailure)
            {
                return Result.Failure<TResponse>(new Error("Instagram.ApiWarning", ex.Message));
            }

            return Result.Failure<TResponse>(
                new Error("Instagram.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            if (allowFailure)
            {
                return Result.Failure<TResponse>(new Error("Instagram.ApiWarning", ex.Message));
            }

            return Result.Failure<TResponse>(
                new Error("Instagram.ParseError", $"JSON parse error: {ex.Message}"));
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

    private static InstagramPostDetails MapPost(InstagramPostDto post)
    {
        return new InstagramPostDetails(
            Id: post.Id ?? string.Empty,
            Caption: post.Caption,
            MediaType: post.MediaType,
            MediaProductType: post.MediaProductType,
            MediaUrl: post.MediaUrl,
            ThumbnailUrl: post.ThumbnailUrl,
            Permalink: post.Permalink,
            Timestamp: post.Timestamp,
            Username: post.Username,
            LikeCount: post.LikeCount,
            CommentCount: post.CommentsCount);
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
            var error = JsonSerializer.Deserialize<InstagramGraphApiErrorResponse>(payload, JsonOptions);
            return error?.Error?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class InstagramPostsApiResponse
    {
        [JsonPropertyName("data")]
        public InstagramPostDto[]? Data { get; set; }

        [JsonPropertyName("paging")]
        public InstagramPagingDto? Paging { get; set; }
    }

    private sealed class InstagramPagingDto
    {
        [JsonPropertyName("cursors")]
        public InstagramPagingCursorDto? Cursors { get; set; }
    }

    private sealed class InstagramPagingCursorDto
    {
        [JsonPropertyName("after")]
        public string? After { get; set; }
    }

    private sealed class InstagramPostDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("caption")]
        public string? Caption { get; set; }

        [JsonPropertyName("media_type")]
        public string? MediaType { get; set; }

        [JsonPropertyName("media_product_type")]
        public string? MediaProductType { get; set; }

        [JsonPropertyName("media_url")]
        public string? MediaUrl { get; set; }

        [JsonPropertyName("thumbnail_url")]
        public string? ThumbnailUrl { get; set; }

        [JsonPropertyName("permalink")]
        public string? Permalink { get; set; }

        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("like_count")]
        public long? LikeCount { get; set; }

        [JsonPropertyName("comments_count")]
        public long? CommentsCount { get; set; }
    }

    private sealed class InstagramInsightsApiResponse
    {
        [JsonPropertyName("data")]
        public InstagramInsightMetricDto[]? Data { get; set; }
    }

    private sealed class InstagramInsightMetricDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("values")]
        public InstagramInsightValueDto[]? Values { get; set; }

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

    private sealed class InstagramInsightValueDto
    {
        [JsonPropertyName("value")]
        public JsonElement Value { get; set; }
    }

    private sealed class InstagramGraphApiErrorResponse
    {
        [JsonPropertyName("error")]
        public InstagramGraphApiError? Error { get; set; }
    }

    private sealed class InstagramGraphApiError
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}