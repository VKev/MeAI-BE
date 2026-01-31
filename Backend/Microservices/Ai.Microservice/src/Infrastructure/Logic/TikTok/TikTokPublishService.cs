using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.TikTok;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.TikTok;

public sealed class TikTokPublishService : ITikTokPublishService
{
    private const string VideoPublishInitEndpoint = "https://open.tiktokapis.com/v2/post/publish/video/init/";
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TikTokPublishService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("TikTok");
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

        var publishResult = await InitiateVideoPublishAsync(
            request.AccessToken,
            request.Caption,
            request.Media.Url,
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
        CancellationToken cancellationToken)
    {
        try
        {
            var requestBody = new TikTokVideoPublishRequest
            {
                PostInfo = new TikTokApiPostInfo
                {
                    Title = caption,
                    PrivacyLevel = "SELF_ONLY",
                    DisableDuet = false,
                    DisableComment = false,
                    DisableStitch = false
                },
                SourceInfo = new TikTokApiSourceInfo
                {
                    Source = "PULL_FROM_URL",
                    VideoUrl = videoUrl
                }
            };

            var jsonContent = JsonSerializer.Serialize(requestBody, JsonOptions);
            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, VideoPublishInitEndpoint);
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            httpRequest.Content = content;

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            var apiResponse = JsonSerializer.Deserialize<TikTokApiVideoInitResponse>(responseBody, JsonOptions);

            if (apiResponse?.Error?.Code != null && apiResponse.Error.Code != "ok")
            {
                return Result.Failure<string>(
                    new Error("TikTok.PublishFailed", apiResponse.Error.Message ?? "TikTok publish failed."));
            }

            if (string.IsNullOrWhiteSpace(apiResponse?.Data?.PublishId))
            {
                return Result.Failure<string>(
                    new Error("TikTok.PublishFailed", "TikTok response did not include a publish id."));
            }

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
    }

    private enum MediaType
    {
        Unknown,
        Video
    }

    #endregion
}
