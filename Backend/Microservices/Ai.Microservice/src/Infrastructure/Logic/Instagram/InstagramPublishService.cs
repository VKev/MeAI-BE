using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Instagram;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Instagram;

public sealed class InstagramPublishService : IInstagramPublishService
{
    private const string GraphApiBaseUrl = "https://graph.facebook.com/v21.0";
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

        var mediaType = ResolveMediaType(request.Media);
        if (mediaType == MediaType.Unknown)
        {
            return Result.Failure<InstagramPublishResult>(
                new Error("Instagram.UnsupportedMedia", "Unsupported Instagram media type."));
        }

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
            ["access_token"] = accessToken,
            ["caption"] = caption
        };

        if (mediaType == MediaType.Image)
        {
            payload["image_url"] = mediaUrl;
        }
        else
        {
            payload["video_url"] = mediaUrl;
            payload["media_type"] = "VIDEO";
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

    private static MediaType ResolveMediaType(InstagramPublishMedia media)
    {
        if (!string.IsNullOrWhiteSpace(media.ContentType))
        {
            if (media.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(media.ContentType, "image", StringComparison.OrdinalIgnoreCase))
            {
                return MediaType.Image;
            }

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
                ".jpg" => MediaType.Image,
                ".jpeg" => MediaType.Image,
                ".png" => MediaType.Image,
                ".gif" => MediaType.Image,
                ".mp4" => MediaType.Video,
                ".mov" => MediaType.Video,
                ".m4v" => MediaType.Video,
                _ => MediaType.Unknown
            };
        }

        return MediaType.Unknown;
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
    }

    private enum MediaType
    {
        Unknown,
        Image,
        Video
    }
}
