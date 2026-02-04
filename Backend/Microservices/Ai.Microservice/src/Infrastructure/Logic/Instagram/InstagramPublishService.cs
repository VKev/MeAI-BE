using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Instagram;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Instagram;

public sealed class InstagramPublishService : IInstagramPublishService
{
    private const string GraphApiBaseUrl = "https://graph.facebook.com/v24.0";
    private const int MediaStatusPollAttempts = 10;
    private static readonly TimeSpan MediaStatusPollDelay = TimeSpan.FromSeconds(3);
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public InstagramPublishService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("Instagram");
    }

    public async Task<Result<InstagramPublishResult>> PublishAsync(
        InstagramPublishRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Result.Failure<InstagramPublishResult>(
                new Error("Instagram.InvalidToken", "Instagram access token is missing."));
        }

        if (string.IsNullOrWhiteSpace(request.InstagramUserId))
        {
            return Result.Failure<InstagramPublishResult>(
                new Error("Instagram.InvalidAccount", "Instagram business account id is missing."));
        }

        if (string.IsNullOrWhiteSpace(request.Media.Url))
        {
            return Result.Failure<InstagramPublishResult>(
                new Error("Instagram.MissingMedia", "Instagram media URL is required."));
        }

        if (!TryValidateMediaUrl(request.Media.Url, out var mediaUrlError))
        {
            return Result.Failure<InstagramPublishResult>(
                new Error("Instagram.InvalidMediaUrl", mediaUrlError));
        }

        var mediaTypeResult = ResolveMediaType(request.Media);
        if (mediaTypeResult.IsFailure)
        {
            return Result.Failure<InstagramPublishResult>(mediaTypeResult.Error);
        }

        var mediaType = mediaTypeResult.Value;

        var creationResult = await CreateMediaContainerAsync(
            request.InstagramUserId,
            request.AccessToken,
            request.Caption,
            request.Media.Url,
            mediaType,
            cancellationToken);

        if (creationResult.IsFailure)
        {
            return Result.Failure<InstagramPublishResult>(creationResult.Error);
        }

        if (mediaType == MediaType.Video)
        {
            var readinessResult = await WaitForMediaReadyAsync(
                creationResult.Value,
                request.AccessToken,
                cancellationToken);

            if (readinessResult.IsFailure)
            {
                return Result.Failure<InstagramPublishResult>(readinessResult.Error);
            }
        }

        var publishResult = await PublishMediaAsync(
            request.InstagramUserId,
            request.AccessToken,
            creationResult.Value,
            cancellationToken);

        if (publishResult.IsFailure)
        {
            return Result.Failure<InstagramPublishResult>(publishResult.Error);
        }

        return Result.Success(new InstagramPublishResult(
            request.InstagramUserId,
            publishResult.Value));
    }

    private async Task<Result<string>> CreateMediaContainerAsync(
        string instagramUserId,
        string accessToken,
        string caption,
        string mediaUrl,
        MediaType mediaType,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, string>
        {
            ["access_token"] = accessToken
        };

        if (!string.IsNullOrWhiteSpace(caption))
        {
            payload["caption"] = caption;
        }

        if (mediaType == MediaType.Image)
        {
            payload["image_url"] = mediaUrl;
        }
        else
        {
            payload["video_url"] = mediaUrl;
            payload["media_type"] = "REELS";
            payload["share_to_feed"] = "true";
        }

        var response = await _httpClient.PostAsync(
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(instagramUserId)}/media",
            new FormUrlEncodedContent(payload),
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<string>(
                new Error("Instagram.MediaCreateFailed", ReadGraphApiError(body) ?? "Failed to create Instagram media."));
        }

        var parsed = JsonSerializer.Deserialize<GraphApiIdResponse>(body, JsonOptions);
        if (string.IsNullOrWhiteSpace(parsed?.Id))
        {
            return Result.Failure<string>(
                new Error("Instagram.MediaCreateFailed", "Instagram response did not include a creation id."));
        }

        return Result.Success(parsed.Id);
    }

    private async Task<Result<string>> PublishMediaAsync(
        string instagramUserId,
        string accessToken,
        string creationId,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, string>
        {
            ["access_token"] = accessToken,
            ["creation_id"] = creationId
        };

        var response = await _httpClient.PostAsync(
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(instagramUserId)}/media_publish",
            new FormUrlEncodedContent(payload),
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<string>(
                new Error("Instagram.PublishFailed", ReadGraphApiError(body) ?? "Failed to publish Instagram media."));
        }

        var parsed = JsonSerializer.Deserialize<GraphApiIdResponse>(body, JsonOptions);
        if (string.IsNullOrWhiteSpace(parsed?.Id))
        {
            return Result.Failure<string>(
                new Error("Instagram.PublishFailed", "Instagram response did not include a post id."));
        }

        return Result.Success(parsed.Id);
    }

    private async Task<Result<bool>> WaitForMediaReadyAsync(
        string creationId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MediaStatusPollAttempts; attempt++)
        {
            var statusResult = await GetMediaStatusAsync(creationId, accessToken, cancellationToken);
            if (statusResult.IsFailure)
            {
                return Result.Failure<bool>(statusResult.Error);
            }

            var status = statusResult.Value.StatusCode;
            if (string.Equals(status, "FINISHED", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Success(true);
            }

            if (string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase))
            {
                var errorMessage = string.IsNullOrWhiteSpace(statusResult.Value.ErrorMessage)
                    ? "Instagram media processing failed."
                    : statusResult.Value.ErrorMessage;
                return Result.Failure<bool>(
                    new Error("Instagram.MediaProcessingFailed", errorMessage));
            }

            await Task.Delay(MediaStatusPollDelay, cancellationToken);
        }

        return Result.Failure<bool>(
            new Error("Instagram.MediaNotReady", "Instagram media is still processing. Please retry shortly."));
    }

    private async Task<Result<MediaStatus>> GetMediaStatusAsync(
        string creationId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(creationId)}?fields=status_code&access_token={Uri.EscapeDataString(accessToken)}",
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<MediaStatus>(
                new Error("Instagram.MediaStatusFailed", ReadGraphApiError(body) ?? "Failed to fetch Instagram media status."));
        }

        var status = TryReadMediaStatus(body);
        if (string.IsNullOrWhiteSpace(status.StatusCode))
        {
            return Result.Failure<MediaStatus>(
                new Error("Instagram.MediaStatusFailed", "Instagram response did not include a status_code."));
        }

        return Result.Success(status);
    }

    private static Result<MediaType> ResolveMediaType(InstagramPublishMedia media)
    {
        var contentType = media.ContentType?.Trim();
        var urlExtension = GetUrlExtension(media.Url);

        var isImageByContentType = !string.IsNullOrWhiteSpace(contentType) &&
                                   (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(contentType, "image", StringComparison.OrdinalIgnoreCase));

        var isVideoByContentType = !string.IsNullOrWhiteSpace(contentType) &&
                                   (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(contentType, "video", StringComparison.OrdinalIgnoreCase));

        var isImageByExtension = urlExtension is ".jpg" or ".jpeg" or ".png";
        var isVideoByExtension = urlExtension is ".mp4";

        if ((isImageByContentType && isVideoByExtension) || (isVideoByContentType && isImageByExtension))
        {
            return Result.Failure<MediaType>(
                new Error("Instagram.MediaTypeMismatch",
                    "Instagram media type does not match the file extension."));
        }

        if (isImageByContentType || isImageByExtension)
        {
            if (!isImageByExtension && !string.IsNullOrWhiteSpace(contentType) &&
                !string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(contentType, "image/jpg", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(contentType, "image", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<MediaType>(
                    new Error("Instagram.UnsupportedMedia",
                        "Instagram supports only JPG or PNG images."));
            }

            return Result.Success(MediaType.Image);
        }

        if (isVideoByContentType || isVideoByExtension)
        {
            if (!isVideoByExtension && !string.IsNullOrWhiteSpace(contentType) &&
                !string.Equals(contentType, "video/mp4", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(contentType, "video", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<MediaType>(
                    new Error("Instagram.UnsupportedMedia",
                        "Instagram supports only MP4 videos."));
            }

            return Result.Success(MediaType.Video);
        }

        if (!string.IsNullOrWhiteSpace(media.ContentType))
        {
            return Result.Failure<MediaType>(
                new Error("Instagram.UnsupportedMedia",
                    "Unsupported Instagram media content type."));
        }

        if (!string.IsNullOrWhiteSpace(media.Url))
        {
            return Result.Failure<MediaType>(
                new Error("Instagram.UnsupportedMedia",
                    "Instagram supports only JPG/PNG images or MP4 videos."));
        }

        return Result.Failure<MediaType>(
            new Error("Instagram.UnsupportedMedia", "Unsupported Instagram media type."));
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
            return FormatGraphApiErrorMessage(error?.Error);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryValidateMediaUrl(string url, out string error)
    {
        error = string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            error = "Instagram media URL must be an absolute URL.";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "Instagram media URL must use HTTPS.";
            return false;
        }

        return true;
    }

    private static string GetUrlExtension(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var cleanUrl = url;
        var queryIndex = cleanUrl.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex > 0)
        {
            cleanUrl = cleanUrl[..queryIndex];
        }

        return Path.GetExtension(cleanUrl).ToLowerInvariant();
    }

    private static MediaStatus TryReadMediaStatus(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new MediaStatus(null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new MediaStatus(null, null);
            }

            var statusCode = (string?)null;
            var errorMessage = (string?)null;

            if (doc.RootElement.TryGetProperty("status_code", out var statusCodeElement))
            {
                statusCode = statusCodeElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("status", out var statusElement))
            {
                statusCode ??= statusElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("error_message", out var error))
            {
                errorMessage = error.GetString();
            }

            return new MediaStatus(statusCode, errorMessage);
        }
        catch (JsonException)
        {
            return new MediaStatus(null, null);
        }
    }

    private sealed record MediaStatus(string? StatusCode, string? ErrorMessage);

    private static string? FormatGraphApiErrorMessage(GraphApiError? error)
    {
        if (error == null)
        {
            return null;
        }

        var mainMessage = !string.IsNullOrWhiteSpace(error.ErrorUserMessage)
            ? error.ErrorUserMessage
            : error.Message;

        if (!string.IsNullOrWhiteSpace(error.ErrorUserTitle))
        {
            mainMessage = string.IsNullOrWhiteSpace(mainMessage)
                ? error.ErrorUserTitle
                : $"{error.ErrorUserTitle}: {mainMessage}";
        }

        var details = new List<string>();

        if (error.Code.HasValue)
        {
            details.Add($"code={error.Code.Value}");
        }

        if (error.ErrorSubcode.HasValue)
        {
            details.Add($"subcode={error.ErrorSubcode.Value}");
        }

        if (!string.IsNullOrWhiteSpace(error.Type))
        {
            details.Add($"type={error.Type}");
        }

        if (!string.IsNullOrWhiteSpace(error.FbTraceId))
        {
            details.Add($"trace={error.FbTraceId}");
        }

        if (details.Count == 0)
        {
            return string.IsNullOrWhiteSpace(mainMessage)
                ? "Graph API request failed."
                : mainMessage;
        }

        return string.IsNullOrWhiteSpace(mainMessage)
            ? $"Graph API request failed ({string.Join(", ", details)})."
            : $"{mainMessage} ({string.Join(", ", details)}).";
    }

    private sealed class GraphApiIdResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
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

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("error_subcode")]
        public int? ErrorSubcode { get; set; }

        [JsonPropertyName("error_user_title")]
        public string? ErrorUserTitle { get; set; }

        [JsonPropertyName("error_user_msg")]
        public string? ErrorUserMessage { get; set; }

        [JsonPropertyName("fbtrace_id")]
        public string? FbTraceId { get; set; }
    }

    private enum MediaType
    {
        Unknown,
        Image,
        Video
    }
}
