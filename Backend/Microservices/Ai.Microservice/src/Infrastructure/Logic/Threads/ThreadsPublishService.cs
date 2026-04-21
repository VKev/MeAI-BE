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

    public async Task<Result<bool>> DeleteAsync(
        ThreadsDeleteRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ThreadsPostId))
        {
            return Result.Failure<bool>(new Error("Threads.DeleteMissingId", "Missing Threads post id."));
        }
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Result.Failure<bool>(new Error("Threads.DeleteMissingToken", "Missing Threads access token."));
        }

        // ExternalContentId may be "{mediaId}|{permalink}" (current format) or raw numeric
        // id (pre-combined-format rows). ExtractMediaIdFromStored handles both.
        var id = ExtractMediaIdFromStored(request.ThreadsPostId);
        var url = $"{GraphApiBaseUrl}/{Uri.EscapeDataString(id)}?access_token={Uri.EscapeDataString(request.AccessToken)}";
        var response = await _httpClient.DeleteAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Treat "not found" / "already deleted" as success — the user's intent is "make
            // it gone" and the platform state matches that outcome. This also rescues legacy
            // rows that stored only a shortcode URL, since DELETE with a shortcode 400s here.
            if (LooksLikeThreadsNotFound(body))
            {
                return Result.Success(true);
            }
            return Result.Failure<bool>(
                new Error("Threads.DeleteFailed", ReadGraphApiError(body) ?? $"Threads delete failed with status {(int)response.StatusCode}: {body}"));
        }
        return Result.Success(true);
    }

    private static bool LooksLikeThreadsNotFound(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        var lower = body.ToLowerInvariant();
        return lower.Contains("does not exist") ||
               lower.Contains("cannot be loaded") ||
               lower.Contains("unsupported delete") ||
               lower.Contains("unknown path components");
    }

    private static string ExtractThreadsId(string raw)
    {
        // If a permalink was stored, pick the last numeric path segment; otherwise return raw.
        if (!raw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }
        try
        {
            var uri = new Uri(raw);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = segments.Length - 1; i >= 0; i--)
            {
                if (long.TryParse(segments[i], out _))
                {
                    return segments[i];
                }
            }
            return segments.Length > 0 ? segments[^1] : raw;
        }
        catch
        {
            return raw;
        }
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

        // Threads' numeric media id is not directly usable in public URLs — the canonical
        // format is https://www.threads.net/@{username}/post/{shortcode}. Ask the Graph API
        // for the permalink so the FE can link out correctly. We encode both the numeric id
        // AND the permalink into PostId as "{mediaId}|{permalink}" — the numeric id is
        // required by DELETE/UPDATE, and the permalink is what the FE shows as "View on
        // Threads". Callers that need only the id split on '|'.
        var permalink = await TryFetchPermalinkAsync(publishResult.Value, request.AccessToken, cancellationToken);
        var combined = string.IsNullOrWhiteSpace(permalink)
            ? publishResult.Value
            : $"{publishResult.Value}|{permalink}";

        return Result.Success(new ThreadsPublishResult(request.ThreadsUserId, combined));
    }

    private static string ExtractMediaIdFromStored(string storedExternalId)
    {
        // stored format (current): "{numericId}|{permalink}"
        // legacy (older rows): raw numeric id OR raw permalink URL
        if (string.IsNullOrWhiteSpace(storedExternalId)) return storedExternalId;
        var pipe = storedExternalId.IndexOf('|');
        if (pipe > 0)
        {
            return storedExternalId[..pipe];
        }
        // Fallback for legacy URL-only rows: pull the last non-empty path segment. This used
        // to return the shortcode (which DELETE doesn't accept) — now at least DELETE will
        // return a clean "not found" and the consumer's 400-tolerant logic will treat it
        // as success so the user isn't stuck.
        return ExtractThreadsId(storedExternalId);
    }

    private async Task<string?> TryFetchPermalinkAsync(
        string mediaId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var url =
                $"{GraphApiBaseUrl}/{Uri.EscapeDataString(mediaId)}?fields=permalink&access_token={Uri.EscapeDataString(accessToken)}";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = JsonSerializer.Deserialize<GraphApiPermalinkResponse>(body, JsonOptions);
            return parsed?.Permalink;
        }
        catch
        {
            return null;
        }
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

    private sealed class GraphApiPermalinkResponse
    {
        [JsonPropertyName("permalink")]
        public string? Permalink { get; set; }
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
