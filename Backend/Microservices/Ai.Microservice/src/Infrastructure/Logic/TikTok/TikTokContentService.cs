using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.TikTok;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.TikTok;

public sealed class TikTokContentService : ITikTokContentService
{
    private const string VideoListEndpoint = "https://open.tiktokapis.com/v2/video/list/";
    private const string VideoQueryEndpoint = "https://open.tiktokapis.com/v2/video/query/";
    private const string VideoFields =
        "id,create_time,cover_image_url,share_url,video_description,duration,title,embed_link,like_count,comment_count,share_count,view_count";

    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TikTokContentService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("TikTok");
    }

    public async Task<Result<TikTokVideoPageResult>> GetVideosAsync(
        TikTokVideoListRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Result.Failure<TikTokVideoPageResult>(
                new Error("TikTok.InvalidToken", "TikTok access token is missing."));
        }

        var payload = new TikTokVideoListPayload
        {
            Cursor = request.Cursor,
            MaxCount = NormalizeMaxCount(request.MaxCount)
        };

        var response = await SendAsync<TikTokVideoListPayload, TikTokVideoListApiResponse>(
            request.AccessToken,
            $"{VideoListEndpoint}?fields={Uri.EscapeDataString(VideoFields)}",
            payload,
            cancellationToken);

        if (response.IsFailure)
        {
            return Result.Failure<TikTokVideoPageResult>(response.Error);
        }

        var data = response.Value.Data;
        if (data == null)
        {
            return Result.Failure<TikTokVideoPageResult>(
                new Error("TikTok.VideoListFailed", "TikTok did not return video data."));
        }

        var videos = (data.Videos ?? Array.Empty<TikTokVideoItemDto>())
            .Select(MapVideo)
            .ToList();

        return Result.Success(new TikTokVideoPageResult(
            Videos: videos,
            Cursor: data.Cursor,
            HasMore: data.HasMore));
    }

    public async Task<Result<TikTokVideoDetails>> GetVideoAsync(
        TikTokVideoDetailsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Result.Failure<TikTokVideoDetails>(
                new Error("TikTok.InvalidToken", "TikTok access token is missing."));
        }

        if (string.IsNullOrWhiteSpace(request.VideoId))
        {
            return Result.Failure<TikTokVideoDetails>(
                new Error("TikTok.InvalidVideoId", "TikTok video id is required."));
        }

        var payload = new TikTokVideoQueryPayload
        {
            Filters = new TikTokVideoFiltersPayload
            {
                VideoIds = new[] { request.VideoId }
            }
        };

        var response = await SendAsync<TikTokVideoQueryPayload, TikTokVideoQueryApiResponse>(
            request.AccessToken,
            $"{VideoQueryEndpoint}?fields={Uri.EscapeDataString(VideoFields)}",
            payload,
            cancellationToken);

        if (response.IsFailure)
        {
            return Result.Failure<TikTokVideoDetails>(response.Error);
        }

        var video = response.Value.Data?.Videos?.FirstOrDefault();
        if (video == null)
        {
            return Result.Failure<TikTokVideoDetails>(
                new Error("TikTok.VideoNotFound", "TikTok video was not found for the current account."));
        }

        return Result.Success(MapVideo(video));
    }

    private async Task<Result<TResponse>> SendAsync<TRequest, TResponse>(
        string accessToken,
        string url,
        TRequest payload,
        CancellationToken cancellationToken)
        where TResponse : TikTokApiResponseBase
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            var parsed = JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
            if (parsed?.Error?.Code != null && !string.Equals(parsed.Error.Code, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<TResponse>(
                    new Error("TikTok.ApiError", $"[{parsed.Error.Code}] {parsed.Error.Message ?? "TikTok API request failed."}"));
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<TResponse>(
                    new Error("TikTok.ApiError", $"TikTok API request failed with status code {(int)response.StatusCode}."));
            }

            if (parsed == null)
            {
                return Result.Failure<TResponse>(
                    new Error("TikTok.ParseError", "Failed to parse TikTok API response."));
            }

            return Result.Success(parsed);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<TResponse>(
                new Error("TikTok.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<TResponse>(
                new Error("TikTok.ParseError", $"JSON parse error: {ex.Message}"));
        }
    }

    private static int NormalizeMaxCount(int? maxCount)
    {
        if (maxCount is null or <= 0)
        {
            return 10;
        }

        return Math.Min(maxCount.Value, 20);
    }

    private static TikTokVideoDetails MapVideo(TikTokVideoItemDto video)
    {
        return new TikTokVideoDetails(
            Id: video.Id ?? string.Empty,
            Title: video.Title,
            VideoDescription: video.VideoDescription,
            CoverImageUrl: video.CoverImageUrl,
            ShareUrl: video.ShareUrl,
            EmbedLink: video.EmbedLink,
            Duration: video.Duration,
            CreateTime: video.CreateTime,
            ViewCount: video.ViewCount,
            LikeCount: video.LikeCount,
            CommentCount: video.CommentCount,
            ShareCount: video.ShareCount);
    }

    private sealed class TikTokVideoListPayload
    {
        [JsonPropertyName("cursor")]
        public long? Cursor { get; set; }

        [JsonPropertyName("max_count")]
        public int MaxCount { get; set; }
    }

    private sealed class TikTokVideoQueryPayload
    {
        [JsonPropertyName("filters")]
        public TikTokVideoFiltersPayload Filters { get; set; } = new();
    }

    private sealed class TikTokVideoFiltersPayload
    {
        [JsonPropertyName("video_ids")]
        public string[] VideoIds { get; set; } = Array.Empty<string>();
    }

    private abstract class TikTokApiResponseBase
    {
        [JsonPropertyName("error")]
        public TikTokApiError? Error { get; set; }
    }

    private sealed class TikTokVideoListApiResponse : TikTokApiResponseBase
    {
        [JsonPropertyName("data")]
        public TikTokVideoListData? Data { get; set; }
    }

    private sealed class TikTokVideoQueryApiResponse : TikTokApiResponseBase
    {
        [JsonPropertyName("data")]
        public TikTokVideoQueryData? Data { get; set; }
    }

    private sealed class TikTokVideoListData
    {
        [JsonPropertyName("videos")]
        public TikTokVideoItemDto[]? Videos { get; set; }

        [JsonPropertyName("cursor")]
        public long? Cursor { get; set; }

        [JsonPropertyName("has_more")]
        public bool HasMore { get; set; }
    }

    private sealed class TikTokVideoQueryData
    {
        [JsonPropertyName("videos")]
        public TikTokVideoItemDto[]? Videos { get; set; }
    }

    private sealed class TikTokVideoItemDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("video_description")]
        public string? VideoDescription { get; set; }

        [JsonPropertyName("cover_image_url")]
        public string? CoverImageUrl { get; set; }

        [JsonPropertyName("share_url")]
        public string? ShareUrl { get; set; }

        [JsonPropertyName("embed_link")]
        public string? EmbedLink { get; set; }

        [JsonPropertyName("duration")]
        public int? Duration { get; set; }

        [JsonPropertyName("create_time")]
        public long? CreateTime { get; set; }

        [JsonPropertyName("view_count")]
        public long? ViewCount { get; set; }

        [JsonPropertyName("like_count")]
        public long? LikeCount { get; set; }

        [JsonPropertyName("comment_count")]
        public long? CommentCount { get; set; }

        [JsonPropertyName("share_count")]
        public long? ShareCount { get; set; }
    }

    private sealed class TikTokApiError
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
