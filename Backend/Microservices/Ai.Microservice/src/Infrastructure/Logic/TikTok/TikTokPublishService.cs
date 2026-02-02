using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.TikTok;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.TikTok;

public sealed class TikTokPublishService : ITikTokPublishService
{
    private const string CreatorInfoEndpoint = "https://open.tiktokapis.com/v2/post/publish/creator_info/query/";
    private const string VideoPublishInitEndpoint = "https://open.tiktokapis.com/v2/post/publish/video/init/";
    private readonly HttpClient _httpClient;
    private readonly ILogger<TikTokPublishService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions JsonOptionsIndented = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public TikTokPublishService(IHttpClientFactory httpClientFactory, ILogger<TikTokPublishService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("TikTok");
        _logger = logger;
    }

    public async Task<Result<TikTokCreatorInfo>> QueryCreatorInfoAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Result.Failure<TikTokCreatorInfo>(
                new Error("TikTok.InvalidToken", "TikTok access token is missing."));
        }

        try
        {
            _logger.LogInformation("[TikTok] Querying creator info at {Endpoint}", CreatorInfoEndpoint);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, CreatorInfoEndpoint);
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            httpRequest.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogInformation("[TikTok] Creator info response: StatusCode={StatusCode}, Body={Body}",
                (int)response.StatusCode, responseBody);

            var apiResponse = JsonSerializer.Deserialize<TikTokApiCreatorInfoResponse>(responseBody, JsonOptions);

            if (apiResponse?.Error?.Code != null && apiResponse.Error.Code != "ok")
            {
                _logger.LogWarning("[TikTok] Creator info failed: Code={Code}, Message={Message}, LogId={LogId}",
                    apiResponse.Error.Code, apiResponse.Error.Message, apiResponse.Error.LogId);

                return Result.Failure<TikTokCreatorInfo>(
                    new Error("TikTok.CreatorInfoFailed", $"[{apiResponse.Error.Code}] {apiResponse.Error.Message ?? "Failed to get creator info."}"));
            }

            if (apiResponse?.Data == null)
            {
                return Result.Failure<TikTokCreatorInfo>(
                    new Error("TikTok.CreatorInfoFailed", "TikTok response did not include creator info."));
            }

            var data = apiResponse.Data;
            return Result.Success(new TikTokCreatorInfo(
                CreatorAvatarUrl: data.CreatorAvatarUrl ?? string.Empty,
                CreatorUsername: data.CreatorUsername ?? string.Empty,
                CreatorNickname: data.CreatorNickname ?? string.Empty,
                PrivacyLevelOptions: data.PrivacyLevelOptions ?? Array.Empty<string>(),
                CommentDisabled: data.CommentDisabled,
                DuetDisabled: data.DuetDisabled,
                StitchDisabled: data.StitchDisabled,
                MaxVideoPostDurationSec: data.MaxVideoPostDurationSec));
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<TikTokCreatorInfo>(
                new Error("TikTok.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<TikTokCreatorInfo>(
                new Error("TikTok.ParseError", $"JSON parse error: {ex.Message}"));
        }
    }

    public async Task<Result<TikTokPublishResult>> PublishAsync(
        TikTokPublishRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Result.Failure<TikTokPublishResult>(
                new Error("TikTok.InvalidToken", "TikTok access token is missing."));
        }

        if (string.IsNullOrWhiteSpace(request.OpenId))
        {
            return Result.Failure<TikTokPublishResult>(
                new Error("TikTok.InvalidAccount", "TikTok open_id is missing."));
        }

        if (string.IsNullOrWhiteSpace(request.Media.Url))
        {
            return Result.Failure<TikTokPublishResult>(
                new Error("TikTok.MissingMedia", "TikTok video URL is required."));
        }

        var mediaType = ResolveMediaType(request.Media);
        if (mediaType != MediaType.Video)
        {
            return Result.Failure<TikTokPublishResult>(
                new Error("TikTok.UnsupportedMedia", "TikTok only supports video publishing."));
        }

        // Query creator info first per TikTok guidelines
        var creatorInfo = request.CreatorInfo;
        if (creatorInfo == null)
        {
            var creatorInfoResult = await QueryCreatorInfoAsync(request.AccessToken, cancellationToken);
            if (creatorInfoResult.IsFailure)
            {
                return Result.Failure<TikTokPublishResult>(creatorInfoResult.Error);
            }
            creatorInfo = creatorInfoResult.Value;
        }

        var publishResult = await InitiateVideoPublishAsync(
            request.AccessToken,
            request.Caption,
            request.Media.Url,
            creatorInfo,
            cancellationToken);

        if (publishResult.IsFailure)
        {
            return Result.Failure<TikTokPublishResult>(publishResult.Error);
        }

        return Result.Success(new TikTokPublishResult(
            request.OpenId,
            publishResult.Value,
            "PROCESSING"));
    }

    private async Task<Result<string>> InitiateVideoPublishAsync(
        string accessToken,
        string caption,
        string videoUrl,
        TikTokCreatorInfo creatorInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            var privacyLevel = creatorInfo.PrivacyLevelOptions.Contains("SELF_ONLY")
                ? "SELF_ONLY"
                : creatorInfo.PrivacyLevelOptions.FirstOrDefault() ?? "SELF_ONLY";

            _logger.LogInformation("[TikTok] Publishing video with privacy_level={PrivacyLevel}, available_options=[{Options}]",
                privacyLevel, string.Join(", ", creatorInfo.PrivacyLevelOptions));

            var requestBody = new TikTokVideoPublishRequest
            {
                PostInfo = new TikTokApiPostInfo
                {
                    Title = caption,
                    PrivacyLevel = privacyLevel,
                    DisableDuet = creatorInfo.DuetDisabled,
                    DisableComment = creatorInfo.CommentDisabled,
                    DisableStitch = creatorInfo.StitchDisabled,
                    BrandContentToggle = false,
                    BrandOrganicToggle = false
                },
                SourceInfo = new TikTokApiSourceInfo
                {
                    Source = "PULL_FROM_URL",
                    VideoUrl = videoUrl
                }
            };

            var jsonContent = JsonSerializer.Serialize(requestBody, JsonOptions);
            
            _logger.LogInformation("[TikTok] Publish request to {Endpoint}: {RequestBody}",
                VideoPublishInitEndpoint, JsonSerializer.Serialize(requestBody, JsonOptionsIndented));

            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, VideoPublishInitEndpoint);
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            httpRequest.Content = content;

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogInformation("[TikTok] Publish response: StatusCode={StatusCode}, Body={Body}",
                (int)response.StatusCode, responseBody);

            var apiResponse = JsonSerializer.Deserialize<TikTokApiVideoInitResponse>(responseBody, JsonOptions);

            if (apiResponse?.Error?.Code != null && apiResponse.Error.Code != "ok")
            {
                _logger.LogWarning("[TikTok] Publish failed: Code={Code}, Message={Message}, LogId={LogId}",
                    apiResponse.Error.Code, apiResponse.Error.Message, apiResponse.Error.LogId);

                var errorMessage = $"[{apiResponse.Error.Code}] {apiResponse.Error.Message ?? "TikTok publish failed."}";
                return Result.Failure<string>(
                    new Error("TikTok.PublishFailed", errorMessage));
            }

            if (string.IsNullOrWhiteSpace(apiResponse?.Data?.PublishId))
            {
                _logger.LogWarning("[TikTok] Publish response missing publish_id");
                return Result.Failure<string>(
                    new Error("TikTok.PublishFailed", "TikTok response did not include a publish id."));
            }

            _logger.LogInformation("[TikTok] Publish initiated successfully: PublishId={PublishId}",
                apiResponse.Data.PublishId);

            return Result.Success(apiResponse.Data.PublishId);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<string>(
                new Error("TikTok.NetworkError", $"Network error: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<string>(
                new Error("TikTok.ParseError", $"JSON parse error: {ex.Message}"));
        }
    }

    private static MediaType ResolveMediaType(TikTokPublishMedia media)
    {
        if (!string.IsNullOrWhiteSpace(media.ContentType))
        {
            if (media.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(media.ContentType, "video", StringComparison.OrdinalIgnoreCase))
            {
                return MediaType.Video;
            }
        }

        if (!string.IsNullOrWhiteSpace(media.Url))
        {
            var cleanUrl = media.Url;
            var queryIndex = cleanUrl.IndexOf('?', StringComparison.Ordinal);
            if (queryIndex > 0)
            {
                cleanUrl = cleanUrl[..queryIndex];
            }

            var extension = Path.GetExtension(cleanUrl).ToLowerInvariant();
            return extension switch
            {
                ".mp4" => MediaType.Video,
                ".mov" => MediaType.Video,
                ".m4v" => MediaType.Video,
                ".webm" => MediaType.Video,
                _ => MediaType.Unknown
            };
        }

        return MediaType.Unknown;
    }

    #region API Models

    private sealed class TikTokApiCreatorInfoResponse
    {
        [JsonPropertyName("data")]
        public TikTokApiCreatorInfoData? Data { get; set; }

        [JsonPropertyName("error")]
        public TikTokApiError? Error { get; set; }
    }

    private sealed class TikTokApiCreatorInfoData
    {
        [JsonPropertyName("creator_avatar_url")]
        public string? CreatorAvatarUrl { get; set; }

        [JsonPropertyName("creator_username")]
        public string? CreatorUsername { get; set; }

        [JsonPropertyName("creator_nickname")]
        public string? CreatorNickname { get; set; }

        [JsonPropertyName("privacy_level_options")]
        public string[]? PrivacyLevelOptions { get; set; }

        [JsonPropertyName("comment_disabled")]
        public bool CommentDisabled { get; set; }

        [JsonPropertyName("duet_disabled")]
        public bool DuetDisabled { get; set; }

        [JsonPropertyName("stitch_disabled")]
        public bool StitchDisabled { get; set; }

        [JsonPropertyName("max_video_post_duration_sec")]
        public int MaxVideoPostDurationSec { get; set; }
    }

    private sealed class TikTokVideoPublishRequest
    {
        [JsonPropertyName("post_info")]
        public TikTokApiPostInfo? PostInfo { get; set; }

        [JsonPropertyName("source_info")]
        public TikTokApiSourceInfo? SourceInfo { get; set; }
    }

    private sealed class TikTokApiPostInfo
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("privacy_level")]
        public string PrivacyLevel { get; set; } = "SELF_ONLY";

        [JsonPropertyName("disable_duet")]
        public bool DisableDuet { get; set; }

        [JsonPropertyName("disable_comment")]
        public bool DisableComment { get; set; }

        [JsonPropertyName("disable_stitch")]
        public bool DisableStitch { get; set; }

        [JsonPropertyName("brand_content_toggle")]
        public bool BrandContentToggle { get; set; }

        [JsonPropertyName("brand_organic_toggle")]
        public bool BrandOrganicToggle { get; set; }
    }

    private sealed class TikTokApiSourceInfo
    {
        [JsonPropertyName("source")]
        public string Source { get; set; } = "PULL_FROM_URL";

        [JsonPropertyName("video_url")]
        public string? VideoUrl { get; set; }
    }

    private sealed class TikTokApiVideoInitResponse
    {
        [JsonPropertyName("data")]
        public TikTokApiVideoInitData? Data { get; set; }

        [JsonPropertyName("error")]
        public TikTokApiError? Error { get; set; }
    }

    private sealed class TikTokApiVideoInitData
    {
        [JsonPropertyName("publish_id")]
        public string? PublishId { get; set; }
    }

    private sealed class TikTokApiError
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("log_id")]
        public string? LogId { get; set; }
    }

    private enum MediaType
    {
        Unknown,
        Video
    }

    #endregion
}

