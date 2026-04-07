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
    private const string UserInfoEndpoint = "https://open.tiktokapis.com/v2/user/info/";
    private const string VideoFields =
        "id,create_time,cover_image_url,share_url,video_description,duration,title,embed_link,like_count,comment_count,share_count,view_count";
    private const string UserFields =
        "open_id,display_name,avatar_url,bio_description,follower_count,following_count,likes_count,video_count";

    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        PropertyNameCaseInsensitive = true
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

        var recentVideo = await TryGetVideoFromRecentListAsync(request.AccessToken, request.VideoId, cancellationToken);
        var mergedVideo = MergeVideoStats(video, recentVideo);

        return Result.Success(MapVideo(mergedVideo));
    }

    public async Task<Result<TikTokAccountInsights>> GetAccountInsightsAsync(
        TikTokAccountInsightsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Result.Failure<TikTokAccountInsights>(
                new Error("TikTok.InvalidToken", "TikTok access token is missing."));
        }

        var response = await SendGetAsync<TikTokUserInfoApiResponse>(
            request.AccessToken,
            $"{UserInfoEndpoint}?fields={Uri.EscapeDataString(UserFields)}",
            cancellationToken,
            allowFailure: true);

        if (response.IsFailure || response.Value.Data?.User == null)
        {
            return Result.Failure<TikTokAccountInsights>(
                new Error("TikTok.ApiWarning", "TikTok account insights are unavailable."));
        }

        var user = response.Value.Data.User;
        return Result.Success(new TikTokAccountInsights(
            OpenId: user.OpenId,
            DisplayName: user.DisplayName,
            AvatarUrl: user.AvatarUrl,
            BioDescription: user.BioDescription,
            FollowerCount: user.FollowerCount,
            FollowingCount: user.FollowingCount,
            LikesCount: user.LikesCount,
            VideoCount: user.VideoCount));
    }

    private async Task<Result<TResponse>> SendAsync<TRequest, TResponse>(
        string accessToken,
        string url,
        TRequest payload,
        CancellationToken cancellationToken,
        bool allowFailure = false)
        where TResponse : TikTokApiResponseBase
    {
        var content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        return await SendCoreAsync<TResponse>(
            HttpMethod.Post,
            accessToken,
            url,
            content,
            cancellationToken,
            allowFailure);
    }

    private async Task<Result<TResponse>> SendGetAsync<TResponse>(
        string accessToken,
        string url,
        CancellationToken cancellationToken,
        bool allowFailure = false)
        where TResponse : TikTokApiResponseBase
    {
        return await SendCoreAsync<TResponse>(
            HttpMethod.Get,
            accessToken,
            url,
            content: null,
            cancellationToken,
            allowFailure);
    }

    private async Task<Result<TResponse>> SendCoreAsync<TResponse>(
        HttpMethod method,
        string accessToken,
        string url,
        HttpContent? content,
        CancellationToken cancellationToken,
        bool allowFailure = false)
        where TResponse : TikTokApiResponseBase
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = content;

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            var parsed = JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
            if (parsed?.Error?.Code != null && !string.Equals(parsed.Error.Code, "ok", StringComparison.OrdinalIgnoreCase))
            {
                if (allowFailure)
                {
                    return Result.Failure<TResponse>(
                        new Error("TikTok.ApiWarning", parsed.Error.Message ?? "Optional TikTok endpoint is unavailable."));
                }

                return Result.Failure<TResponse>(
                    new Error("TikTok.ApiError", $"[{parsed.Error.Code}] {parsed.Error.Message ?? "TikTok API request failed."}"));
            }

            if (!response.IsSuccessStatusCode)
            {
                if (allowFailure)
                {
                    return Result.Failure<TResponse>(
                        new Error("TikTok.ApiWarning", $"Optional TikTok endpoint failed with status code {(int)response.StatusCode}."));
                }

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
            if (allowFailure)
            {
                return Result.Failure<TResponse>(
                    new Error("TikTok.ApiWarning", ex.Message));
            }

            return Result.Failure<TResponse>(
                new Error("TikTok.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            if (allowFailure)
            {
                return Result.Failure<TResponse>(
                    new Error("TikTok.ApiWarning", ex.Message));
            }

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

    private async Task<TikTokVideoItemDto?> TryGetVideoFromRecentListAsync(
        string accessToken,
        string videoId,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<TikTokVideoListPayload, TikTokVideoListApiResponse>(
            accessToken,
            $"{VideoListEndpoint}?fields={Uri.EscapeDataString(VideoFields)}",
            new TikTokVideoListPayload
            {
                Cursor = null,
                MaxCount = 20
            },
            cancellationToken,
            allowFailure: true);

        if (response.IsFailure)
        {
            return null;
        }

        return response.Value.Data?.Videos?.FirstOrDefault(item =>
            string.Equals(item.Id, videoId, StringComparison.Ordinal));
    }

    private static TikTokVideoItemDto MergeVideoStats(
        TikTokVideoItemDto primary,
        TikTokVideoItemDto? fallback)
    {
        if (fallback == null)
        {
            return primary;
        }

        return new TikTokVideoItemDto
        {
            Id = primary.Id ?? fallback.Id,
            Title = primary.Title ?? fallback.Title,
            VideoDescription = primary.VideoDescription ?? fallback.VideoDescription,
            CoverImageUrl = primary.CoverImageUrl ?? fallback.CoverImageUrl,
            ShareUrl = primary.ShareUrl ?? fallback.ShareUrl,
            EmbedLink = primary.EmbedLink ?? fallback.EmbedLink,
            Duration = primary.Duration ?? fallback.Duration,
            CreateTime = primary.CreateTime ?? fallback.CreateTime,
            ViewCount = MaxCount(primary.ViewCount, fallback.ViewCount),
            LikeCount = MaxCount(primary.LikeCount, fallback.LikeCount),
            CommentCount = MaxCount(primary.CommentCount, fallback.CommentCount),
            ShareCount = MaxCount(primary.ShareCount, fallback.ShareCount)
        };
    }

    private static long? MaxCount(long? first, long? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return Math.Max(first.Value, second.Value);
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

    private sealed class TikTokUserInfoApiResponse : TikTokApiResponseBase
    {
        [JsonPropertyName("data")]
        public TikTokUserInfoDataDto? Data { get; set; }
    }

    private sealed class TikTokUserInfoDataDto
    {
        [JsonPropertyName("user")]
        public TikTokUserDto? User { get; set; }
    }

    private sealed class TikTokUserDto
    {
        [JsonPropertyName("open_id")]
        public string? OpenId { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("avatar_url")]
        public string? AvatarUrl { get; set; }

        [JsonPropertyName("bio_description")]
        public string? BioDescription { get; set; }

        [JsonPropertyName("follower_count")]
        public long? FollowerCount { get; set; }

        [JsonPropertyName("following_count")]
        public long? FollowingCount { get; set; }

        [JsonPropertyName("likes_count")]
        public long? LikesCount { get; set; }

        [JsonPropertyName("video_count")]
        public long? VideoCount { get; set; }
    }

    private sealed class TikTokApiError
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
