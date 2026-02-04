using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Threads;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Threads;

public sealed class ThreadsPublishService : IThreadsPublishService
{
    private const string GraphApiBaseUrl = "https://graph.threads.net/v1.0";
    private static readonly TimeSpan VideoStatusPollDelay = TimeSpan.FromSeconds(4);
    private const int VideoStatusMaxAttempts = 30;
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public ThreadsPublishService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("Threads");
    }

    public async Task<Result<ThreadsPublishResult>> PublishAsync(
        ThreadsPublishRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Result.Failure<ThreadsPublishResult>(
                new Error("Threads.InvalidToken", "Threads access token is missing."));
        }

        if (string.IsNullOrWhiteSpace(request.ThreadsUserId))
        {
            return Result.Failure<ThreadsPublishResult>(
                new Error("Threads.InvalidAccount", "Threads user id is missing."));
        }

        if (string.IsNullOrWhiteSpace(request.Text) && request.Media is null)
        {
            return Result.Failure<ThreadsPublishResult>(
                new Error("Threads.MissingContent", "Threads post content is empty."));
        }

        var mediaType = ResolveMediaType(request.Media);
        if (request.Media is not null && mediaType == MediaType.Unknown)
        {
            return Result.Failure<ThreadsPublishResult>(
                new Error("Threads.UnsupportedMedia", "Unsupported Threads media type."));
        }

        var creationResult = await CreateThreadContainerAsync(
            request.ThreadsUserId,
            request.AccessToken,
            request.Text,
            request.Media,
            mediaType,
            cancellationToken);

        if (creationResult.IsFailure)
        {
            return Result.Failure<ThreadsPublishResult>(creationResult.Error);
        }

        var publishResult = await PublishThreadAsync(
            request.ThreadsUserId,
            request.AccessToken,
            creationResult.Value,
            mediaType,
            cancellationToken);

        if (publishResult.IsFailure)
        {
            return Result.Failure<ThreadsPublishResult>(publishResult.Error);
        }

        return Result.Success(new ThreadsPublishResult(
            request.ThreadsUserId,
            publishResult.Value));
    }

    private async Task<Result<string>> CreateThreadContainerAsync(
        string threadsUserId,
        string accessToken,
        string text,
        ThreadsPublishMedia? media,
        MediaType mediaType,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, string>
        {
            ["access_token"] = accessToken,
            ["text"] = text
        };

        if (media is null)
        {
            payload["media_type"] = "TEXT";
        }
        else if (mediaType == MediaType.Image)
        {
            payload["media_type"] = "IMAGE";
            payload["image_url"] = media.Url;
        }
        else if (mediaType == MediaType.Video)
        {
            payload["media_type"] = "VIDEO";
            payload["video_url"] = media.Url;
        }

        var response = await _httpClient.PostAsync(
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(threadsUserId)}/threads",
            new FormUrlEncodedContent(payload),
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<string>(
                new Error("Threads.CreateFailed", ReadGraphApiError(body) ?? "Failed to create Threads container."));
        }

        var parsed = JsonSerializer.Deserialize<GraphApiIdResponse>(body, JsonOptions);
        if (string.IsNullOrWhiteSpace(parsed?.Id))
        {
            return Result.Failure<string>(
                new Error("Threads.CreateFailed", "Threads response did not include a creation id."));
        }

        return Result.Success(parsed.Id);
    }

    private async Task<Result<string>> PublishThreadAsync(
        string threadsUserId,
        string accessToken,
        string creationId,
        MediaType mediaType,
        CancellationToken cancellationToken)
    {
        if (mediaType == MediaType.Video)
        {
            var waitResult = await WaitForVideoContainerAsync(
                accessToken,
                creationId,
                cancellationToken);

            if (waitResult.IsFailure)
            {
                return Result.Failure<string>(waitResult.Error);
            }
        }

        var payload = new Dictionary<string, string>
        {
            ["access_token"] = accessToken,
            ["creation_id"] = creationId
        };

        var response = await _httpClient.PostAsync(
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(threadsUserId)}/threads_publish",
            new FormUrlEncodedContent(payload),
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<string>(
                new Error("Threads.PublishFailed", ReadGraphApiError(body) ?? "Failed to publish Threads post."));
        }

        var parsed = JsonSerializer.Deserialize<GraphApiIdResponse>(body, JsonOptions);
        if (string.IsNullOrWhiteSpace(parsed?.Id))
        {
            return Result.Failure<string>(
                new Error("Threads.PublishFailed", "Threads response did not include a post id."));
        }

        return Result.Success(parsed.Id);
    }

    private async Task<Result<bool>> WaitForVideoContainerAsync(
        string accessToken,
        string creationId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < VideoStatusMaxAttempts; attempt++)
        {
            var statusResult = await GetContainerStatusAsync(accessToken, creationId, cancellationToken);
            if (statusResult.IsFailure)
            {
                return Result.Failure<bool>(statusResult.Error);
            }

            var status = statusResult.Value.Status;
            if (string.Equals(status, "FINISHED", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Success(true);
            }

            if (string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase))
            {
                var message = string.IsNullOrWhiteSpace(statusResult.Value.ErrorMessage)
                    ? "Threads video processing failed."
                    : statusResult.Value.ErrorMessage;
                return Result.Failure<bool>(new Error("Threads.VideoProcessingFailed", message));
            }

            if (string.Equals(status, "EXPIRED", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<bool>(
                    new Error("Threads.VideoExpired", "Threads video container expired before publishing."));
            }

            await Task.Delay(VideoStatusPollDelay, cancellationToken);
        }

        return Result.Failure<bool>(
            new Error("Threads.VideoProcessingTimeout", "Threads video is still processing. Try publishing again shortly."));
    }

    private async Task<Result<GraphApiStatusResponse>> GetContainerStatusAsync(
        string accessToken,
        string creationId,
        CancellationToken cancellationToken)
    {
        var requestUrl =
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(creationId)}?fields=id,status,error_message&access_token={Uri.EscapeDataString(accessToken)}";

        var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<GraphApiStatusResponse>(
                new Error("Threads.StatusFailed", ReadGraphApiError(body) ?? "Failed to fetch Threads container status."));
        }

        var parsed = JsonSerializer.Deserialize<GraphApiStatusResponse>(body, JsonOptions);
        if (parsed is null || string.IsNullOrWhiteSpace(parsed.Status))
        {
            return Result.Failure<GraphApiStatusResponse>(
                new Error("Threads.StatusFailed", "Threads status response was invalid."));
        }

        return Result.Success(parsed);
    }

    private static MediaType ResolveMediaType(ThreadsPublishMedia? media)
    {
        if (media is null)
        {
            return MediaType.None;
        }

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

    private sealed class GraphApiStatusResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }
    }

    private enum MediaType
    {
        None,
        Image,
        Video,
        Unknown
    }
}
