using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Threads;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Threads;

public sealed class ThreadsPublishService : IThreadsPublishService
{
    private const string GraphApiBaseUrl = "https://graph.threads.net/v1.0";
    private const int MaxTextLength = 500;
    private static readonly TimeSpan VideoStatusPollDelay = TimeSpan.FromSeconds(4);
    private const int VideoStatusMaxAttempts = 30;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ThreadsPublishService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public ThreadsPublishService(
        IHttpClientFactory httpClientFactory,
        ILogger<ThreadsPublishService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Threads");
        _logger = logger;
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

            _logger.LogWarning(
                "Threads delete failed. ThreadsPostId={ThreadsPostId}, MediaId={MediaId}, StatusCode={StatusCode}, Error={Error}, Body={Body}",
                request.ThreadsPostId,
                id,
                (int)response.StatusCode,
                ReadGraphApiError(body),
                TruncateForLog(body));

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

        var mediaItems = request.Media?
            .Where(media => media is not null)
            .ToList() ?? [];

        if (string.IsNullOrWhiteSpace(request.Text) && mediaItems.Count == 0)
        {
            return Result.Failure<ThreadsPublishResult>(
                new Error("Threads.MissingContent", "Threads post content is empty."));
        }

        var mediaTypes = mediaItems
            .Select(ResolveMediaType)
            .ToList();
        if (mediaTypes.Any(mediaType => mediaType == MediaType.Unknown))
        {
            return Result.Failure<ThreadsPublishResult>(
                new Error("Threads.UnsupportedMedia", "Unsupported Threads media type."));
        }

        var text = NormalizeTextForThreads(request.Text);
        if (text.Length != request.Text.Length)
        {
            _logger.LogInformation(
                "Threads text was capped before publish. ThreadsUserId={ThreadsUserId}, OriginalLength={OriginalLength}, SentLength={SentLength}",
                request.ThreadsUserId,
                request.Text.Length,
                text.Length);
        }

        var mediaType = mediaItems.Count > 1
            ? MediaType.Carousel
            : mediaTypes.FirstOrDefault(MediaType.None);

        var creationResult = mediaItems.Count > 1
            ? await CreateCarouselContainerAsync(
                request.ThreadsUserId,
                request.AccessToken,
                text,
                mediaItems,
                mediaTypes,
                cancellationToken)
            : await CreateThreadContainerAsync(
                request.ThreadsUserId,
                request.AccessToken,
                text,
                mediaItems.Count == 1 ? mediaItems[0] : null,
                mediaType,
                isCarouselItem: false,
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

    private static string NormalizeTextForThreads(string text)
    {
        if (text.Length <= MaxTextLength)
        {
            return text;
        }

        var truncated = text[..MaxTextLength];
        if (truncated.Length > 0 && char.IsHighSurrogate(truncated[^1]))
        {
            truncated = truncated[..^1];
        }

        return truncated.TrimEnd();
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
        bool isCarouselItem,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, string>
        {
            ["access_token"] = accessToken
        };

        if (!string.IsNullOrWhiteSpace(text))
        {
            payload["text"] = text;
        }

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

        if (isCarouselItem)
        {
            payload["is_carousel_item"] = "true";
        }

        var response = await _httpClient.PostAsync(
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(threadsUserId)}/threads",
            new FormUrlEncodedContent(payload),
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = ReadGraphApiError(body);
            _logger.LogWarning(
                "Threads create-container failed. ThreadsUserId={ThreadsUserId}, StatusCode={StatusCode}, MediaType={MediaType}, TextLength={TextLength}, Error={Error}, Body={Body}",
                threadsUserId,
                (int)response.StatusCode,
                mediaType,
                text.Length,
                errorMessage,
                TruncateForLog(body));

            return Result.Failure<string>(
                new Error("Threads.CreateFailed", errorMessage ?? "Failed to create Threads container."));
        }

        var parsed = JsonSerializer.Deserialize<GraphApiIdResponse>(body, JsonOptions);
        if (string.IsNullOrWhiteSpace(parsed?.Id))
        {
            return Result.Failure<string>(
                new Error("Threads.CreateFailed", "Threads response did not include a creation id."));
        }

        return Result.Success(parsed.Id);
    }

    private async Task<Result<string>> CreateCarouselContainerAsync(
        string threadsUserId,
        string accessToken,
        string text,
        IReadOnlyList<ThreadsPublishMedia> mediaItems,
        IReadOnlyList<MediaType> mediaTypes,
        CancellationToken cancellationToken)
    {
        var childIds = new List<string>(mediaItems.Count);
        for (var i = 0; i < mediaItems.Count; i++)
        {
            var childResult = await CreateThreadContainerAsync(
                threadsUserId,
                accessToken,
                text: string.Empty,
                mediaItems[i],
                mediaTypes[i],
                isCarouselItem: true,
                cancellationToken);

            if (childResult.IsFailure)
            {
                return Result.Failure<string>(childResult.Error);
            }

            if (mediaTypes[i] == MediaType.Video)
            {
                var waitResult = await WaitForContainerAsync(
                    accessToken,
                    childResult.Value,
                    mediaTypes[i],
                    cancellationToken);

                if (waitResult.IsFailure)
                {
                    return Result.Failure<string>(waitResult.Error);
                }
            }

            childIds.Add(childResult.Value);
        }

        var payload = new Dictionary<string, string>
        {
            ["access_token"] = accessToken,
            ["media_type"] = "CAROUSEL",
            ["children"] = string.Join(",", childIds)
        };

        if (!string.IsNullOrWhiteSpace(text))
        {
            payload["text"] = text;
        }

        var response = await _httpClient.PostAsync(
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(threadsUserId)}/threads",
            new FormUrlEncodedContent(payload),
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = ReadGraphApiError(body);
            _logger.LogWarning(
                "Threads create-carousel-container failed. ThreadsUserId={ThreadsUserId}, StatusCode={StatusCode}, MediaCount={MediaCount}, TextLength={TextLength}, Error={Error}, Body={Body}",
                threadsUserId,
                (int)response.StatusCode,
                mediaItems.Count,
                text.Length,
                errorMessage,
                TruncateForLog(body));

            return Result.Failure<string>(
                new Error("Threads.CreateCarouselFailed", errorMessage ?? "Failed to create Threads carousel container."));
        }

        var parsed = JsonSerializer.Deserialize<GraphApiIdResponse>(body, JsonOptions);
        if (string.IsNullOrWhiteSpace(parsed?.Id))
        {
            return Result.Failure<string>(
                new Error("Threads.CreateCarouselFailed", "Threads response did not include a carousel creation id."));
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
        if (mediaType is MediaType.Video or MediaType.Carousel)
        {
            var waitResult = await WaitForContainerAsync(
                accessToken,
                creationId,
                mediaType,
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
            var errorMessage = ReadGraphApiError(body);
            _logger.LogWarning(
                "Threads publish failed. ThreadsUserId={ThreadsUserId}, CreationId={CreationId}, StatusCode={StatusCode}, MediaType={MediaType}, Error={Error}, Body={Body}",
                threadsUserId,
                creationId,
                (int)response.StatusCode,
                mediaType,
                errorMessage,
                TruncateForLog(body));

            return Result.Failure<string>(
                new Error("Threads.PublishFailed", errorMessage ?? "Failed to publish Threads post."));
        }

        var parsed = JsonSerializer.Deserialize<GraphApiIdResponse>(body, JsonOptions);
        if (string.IsNullOrWhiteSpace(parsed?.Id))
        {
            return Result.Failure<string>(
                new Error("Threads.PublishFailed", "Threads response did not include a post id."));
        }

        return Result.Success(parsed.Id);
    }

    private async Task<Result<bool>> WaitForContainerAsync(
        string accessToken,
        string creationId,
        MediaType mediaType,
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
                    ? mediaType == MediaType.Video
                        ? "Threads video processing failed."
                        : "Threads media container processing failed."
                    : statusResult.Value.ErrorMessage;
                _logger.LogWarning(
                    "Threads container processing failed. CreationId={CreationId}, MediaType={MediaType}, Status={Status}, Error={Error}",
                    creationId,
                    mediaType,
                    status,
                    message);
                return Result.Failure<bool>(new Error(
                    mediaType == MediaType.Video ? "Threads.VideoProcessingFailed" : "Threads.ContainerProcessingFailed",
                    message));
            }

            if (string.Equals(status, "EXPIRED", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Threads container expired before publish. CreationId={CreationId}, MediaType={MediaType}, Status={Status}",
                    creationId,
                    mediaType,
                    status);
                return Result.Failure<bool>(
                    new Error(
                        mediaType == MediaType.Video ? "Threads.VideoExpired" : "Threads.ContainerExpired",
                        mediaType == MediaType.Video
                            ? "Threads video container expired before publishing."
                            : "Threads media container expired before publishing."));
            }

            await Task.Delay(VideoStatusPollDelay, cancellationToken);
        }

        return Result.Failure<bool>(
            new Error(
                mediaType == MediaType.Video ? "Threads.VideoProcessingTimeout" : "Threads.ContainerProcessingTimeout",
                mediaType == MediaType.Video
                    ? "Threads video is still processing. Try publishing again shortly."
                    : "Threads media container is still processing. Try publishing again shortly."));
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
            var errorMessage = ReadGraphApiError(body);
            _logger.LogWarning(
                "Threads status fetch failed. CreationId={CreationId}, StatusCode={StatusCode}, Error={Error}, Body={Body}",
                creationId,
                (int)response.StatusCode,
                errorMessage,
                TruncateForLog(body));

            return Result.Failure<GraphApiStatusResponse>(
                new Error("Threads.StatusFailed", errorMessage ?? "Failed to fetch Threads container status."));
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

    private static string TruncateForLog(string? value, int max = 1000)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value[..max] + "...";
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
        Carousel,
        Unknown
    }
}
