using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Facebook;
using Microsoft.Extensions.Caching.Memory;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Facebook;

public sealed class FacebookPublishService : IFacebookPublishService
{
    private const string GraphApiBaseUrl = "https://graph.facebook.com/v25.0";
    // Cache /me/accounts results briefly to avoid burning the FB app rate limit during
    // rapid publish/unpublish/delete bursts. 60s is short enough that a newly-added page
    // shows up quickly; long enough to collapse the token-resolution calls a single
    // publish flow makes across multiple targets.
    private static readonly TimeSpan PageListCacheTtl = TimeSpan.FromSeconds(60);
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _memoryCache;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public FacebookPublishService(IHttpClientFactory httpClientFactory, IMemoryCache memoryCache)
    {
        _httpClient = httpClientFactory.CreateClient("Facebook");
        _memoryCache = memoryCache;
    }

    private static string BuildPageCacheKey(string userAccessToken)
    {
        // Hash the token so it never lands in logs / dumps if the memory cache is inspected.
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(userAccessToken));
        return $"fb:pages:{Convert.ToHexString(hash)}";
    }

    public async Task<Result<bool>> DeleteAsync(
        FacebookDeleteRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ExternalPostId))
        {
            return Result.Failure<bool>(new Error("Facebook.DeleteMissingId", "Missing Facebook post id."));
        }

        // Facebook publish fans out to every page the user token has access to, so a post
        // may live on a DIFFERENT page than the SocialMedia row we're unpublishing from.
        // Resolve the correct page token by parsing `pageId_postId` and matching via
        // `/me/accounts` when the caller's page token doesn't match the owning page.
        var tokenResult = await ResolvePageTokenForPostAsync(
            request.ExternalPostId, request.PageAccessToken, request.UserAccessToken, cancellationToken);

        if (tokenResult.IsFailure)
        {
            return Result.Failure<bool>(tokenResult.Error);
        }

        // Facebook Reels are videos. The Graph API accepts DELETE on `{pageId}_{videoId}` with
        // a 200 {success:true} response but DOESN'T actually remove the reel — you must DELETE
        // the bare `{videoId}`. We still parse the pageId prefix for token resolution above,
        // so here we strip it before hitting the API.
        var deleteId = request.ExternalPostId;
        if (request.IsReel)
        {
            var underscoreIdx = request.ExternalPostId.IndexOf('_');
            if (underscoreIdx > 0)
            {
                deleteId = request.ExternalPostId[(underscoreIdx + 1)..];
            }
        }

        var url = $"{GraphApiBaseUrl}/{Uri.EscapeDataString(deleteId)}?access_token={Uri.EscapeDataString(tokenResult.Value)}";
        var response = await _httpClient.DeleteAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Treat "post not found" (already deleted, or stale external id) as success —
            // the user's intent is "make it gone" and the platform state already matches.
            if ((int)response.StatusCode == 400 && LooksLikeNotFound(body))
            {
                return Result.Success(true);
            }
            return Result.Failure<bool>(
                new Error("Facebook.DeleteFailed", ReadGraphApiError(body) ?? $"Delete failed with status {(int)response.StatusCode}: {body}"));
        }
        return Result.Success(true);
    }

    public async Task<Result<bool>> UpdateAsync(
        FacebookUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ExternalPostId))
        {
            return Result.Failure<bool>(new Error("Facebook.UpdateMissingId", "Missing Facebook post id."));
        }

        var tokenResult = await ResolvePageTokenForPostAsync(
            request.ExternalPostId, request.PageAccessToken, request.UserAccessToken, cancellationToken);

        if (tokenResult.IsFailure)
        {
            return Result.Failure<bool>(tokenResult.Error);
        }

        var url = $"{GraphApiBaseUrl}/{Uri.EscapeDataString(request.ExternalPostId)}";
        var payload = new Dictionary<string, string>
        {
            ["access_token"] = tokenResult.Value,
            ["message"] = request.Message ?? string.Empty
        };
        var response = await _httpClient.PostAsync(url, new FormUrlEncodedContent(payload), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<bool>(
                new Error("Facebook.UpdateFailed", ReadGraphApiError(body) ?? $"Update failed with status {(int)response.StatusCode}: {body}"));
        }
        return Result.Success(true);
    }

    private async Task<Result<string>> ResolvePageTokenForPostAsync(
        string externalPostId,
        string? hintedPageAccessToken,
        string? userAccessToken,
        CancellationToken cancellationToken)
    {
        // externalPostId format: {pageId}_{postId}. If we can extract the page id AND we have a
        // user token, we can look up the correct page token from `/me/accounts`. That way
        // a SocialMedia row whose page_access_token belongs to page A can still moderate a
        // post that actually lives on page B (since publish fans out to every linked page).
        var underscore = externalPostId.IndexOf('_');
        var pageId = underscore > 0 ? externalPostId[..underscore] : null;

        if (!string.IsNullOrWhiteSpace(pageId) && !string.IsNullOrWhiteSpace(userAccessToken))
        {
            var pages = await FetchPagesAsync(userAccessToken, cancellationToken);
            if (pages.IsSuccess)
            {
                var match = pages.Value.FirstOrDefault(p => p.PageId == pageId);
                if (match is not null && !string.IsNullOrWhiteSpace(match.PageAccessToken))
                {
                    return Result.Success(match.PageAccessToken);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(hintedPageAccessToken))
        {
            return Result.Success(hintedPageAccessToken);
        }

        return Result.Failure<string>(
            new Error("Facebook.NoPageToken", "Could not resolve a Facebook page access token for this post."));
    }

    private static bool LooksLikeNotFound(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        var lower = body.ToLowerInvariant();
        return lower.Contains("does not exist") ||
               lower.Contains("cannot be loaded") ||
               lower.Contains("unknown path components") ||
               lower.Contains("no node") ||
               lower.Contains("\"code\":803") ||
               lower.Contains("\"code\":100");
    }

    public async Task<Result<IReadOnlyList<FacebookPublishResult>>> PublishAsync(
        FacebookPublishRequest request,
        CancellationToken cancellationToken)
    {
        var resolveResult = await ResolvePagesAsync(request, cancellationToken);
        if (resolveResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<FacebookPublishResult>>(
                resolveResult.Error);
        }

        var pages = resolveResult.Value;

        var (videos, images, invalidMedia) = SplitMedia(request.Media);

        var wantsReel = !string.IsNullOrWhiteSpace(request.PostType) &&
                        string.Equals(request.PostType, "reels", StringComparison.OrdinalIgnoreCase);

        if (!wantsReel && videos.Count == 0 && images.Count == 0)
        {
            var textResults = new List<FacebookPublishResult>();
            foreach (var page in pages)
            {
                var publishResult = await PublishFeedAsync(
                    page.PageId,
                    page.PageAccessToken,
                    request.Message,
                    [],
                    cancellationToken);

                if (publishResult.IsFailure)
                {
                    return Result.Failure<IReadOnlyList<FacebookPublishResult>>(publishResult.Error);
                }

                textResults.Add(new FacebookPublishResult(page.PageId, publishResult.Value));
            }

            return Result.Success<IReadOnlyList<FacebookPublishResult>>(textResults);
        }

        // Reels must be exactly one video. Reject images-for-reel loudly so the user gets a
        // clear error instead of silently falling through to the regular /photos feed path.
        if (wantsReel)
        {
            if (videos.Count == 0)
            {
                return Result.Failure<IReadOnlyList<FacebookPublishResult>>(
                    new Error("Facebook.ReelRequiresVideo",
                        "Facebook Reels require a single video — images are not supported."));
            }
            if (videos.Count > 1 || images.Count > 0)
            {
                return Result.Failure<IReadOnlyList<FacebookPublishResult>>(
                    new Error("Facebook.ReelSingleVideo",
                        "Facebook Reels only support a single video."));
            }
        }

        if (invalidMedia.Count > 0)
        {
            return Result.Failure<IReadOnlyList<FacebookPublishResult>>(
                new Error("Facebook.UnsupportedMedia", "Unsupported media type for Facebook publishing."));
        }

        if (videos.Count > 0 && images.Count > 0)
        {
            return Result.Failure<IReadOnlyList<FacebookPublishResult>>(
                new Error("Facebook.MixedMedia", "Facebook posts cannot mix images and videos."));
        }

        if (videos.Count > 1)
        {
            return Result.Failure<IReadOnlyList<FacebookPublishResult>>(
                new Error("Facebook.MultiVideo", "Facebook posts support only one video per publish."));
        }

        if (videos.Count == 1)
        {
            var isReel = !string.IsNullOrWhiteSpace(request.PostType) &&
                         string.Equals(request.PostType, "reels", StringComparison.OrdinalIgnoreCase);

            var results = new List<FacebookPublishResult>();
            foreach (var page in pages)
            {
                var publishResult = isReel
                    ? await PublishReelAsync(
                        page.PageId,
                        page.PageAccessToken,
                        request.Message,
                        videos[0],
                        cancellationToken)
                    : await PublishVideoAsync(
                        page.PageId,
                        page.PageAccessToken,
                        request.Message,
                        videos[0],
                        cancellationToken);

                if (publishResult.IsFailure)
                {
                    return Result.Failure<IReadOnlyList<FacebookPublishResult>>(publishResult.Error);
                }

                results.Add(new FacebookPublishResult(page.PageId, publishResult.Value.PostId));
            }

            return Result.Success<IReadOnlyList<FacebookPublishResult>>(results);
        }

        var imageResults = new List<FacebookPublishResult>();
        foreach (var page in pages)
        {
            var publishResult = await PublishImagesAsync(
                page.PageId,
                page.PageAccessToken,
                request.Message,
                images,
                cancellationToken);

            if (publishResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<FacebookPublishResult>>(publishResult.Error);
            }

            imageResults.Add(new FacebookPublishResult(page.PageId, publishResult.Value.PostId));
        }

        return Result.Success<IReadOnlyList<FacebookPublishResult>>(imageResults);
    }

    private async Task<Result<FacebookPublishResult>> PublishImagesAsync(
        string pageId,
        string pageAccessToken,
        string message,
        IReadOnlyList<FacebookPublishMedia> images,
        CancellationToken cancellationToken)
    {
        var mediaIds = new List<string>();

        foreach (var image in images)
        {
            var uploadResult = await UploadPhotoAsync(pageId, pageAccessToken, image, cancellationToken);
            if (uploadResult.IsFailure)
            {
                return Result.Failure<FacebookPublishResult>(uploadResult.Error);
            }

            mediaIds.Add(uploadResult.Value);
        }

        var publishResult = await PublishFeedAsync(pageId, pageAccessToken, message, mediaIds, cancellationToken);
        if (publishResult.IsFailure)
        {
            return Result.Failure<FacebookPublishResult>(publishResult.Error);
        }

        return Result.Success(new FacebookPublishResult(pageId, publishResult.Value));
    }

    private async Task<Result<FacebookPublishResult>> PublishVideoAsync(
        string pageId,
        string pageAccessToken,
        string message,
        FacebookPublishMedia video,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, string>
        {
            ["file_url"] = video.Url,
            ["description"] = message,
            ["access_token"] = pageAccessToken
        };

        var response = await _httpClient.PostAsync(
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(pageId)}/videos",
            new FormUrlEncodedContent(payload),
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<FacebookPublishResult>(
                new Error("Facebook.PublishFailed", ReadGraphApiError(body) ?? "Failed to publish video to Facebook."));
        }

        var parsed = JsonSerializer.Deserialize<GraphApiIdResponse>(body, JsonOptions);
        if (string.IsNullOrWhiteSpace(parsed?.Id))
        {
            return Result.Failure<FacebookPublishResult>(
                new Error("Facebook.PublishFailed", "Facebook response did not include a post id."));
        }

        return Result.Success(new FacebookPublishResult(pageId, parsed.Id));
    }

    // Facebook Reels use a 3-phase resumable upload:
    //   1. POST /{page_id}/video_reels?upload_phase=start         -> returns video_id + upload_url
    //   2. POST {upload_url} with header file_url={presigned}     -> FB hosted-file fetch
    //   3. POST /{page_id}/video_reels?upload_phase=finish&...    -> publishes the reel
    private async Task<Result<FacebookPublishResult>> PublishReelAsync(
        string pageId,
        string pageAccessToken,
        string message,
        FacebookPublishMedia video,
        CancellationToken cancellationToken)
    {
        // Phase 1: start
        var startPayload = new Dictionary<string, string>
        {
            ["upload_phase"] = "start",
            ["access_token"] = pageAccessToken
        };

        var startResponse = await _httpClient.PostAsync(
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(pageId)}/video_reels",
            new FormUrlEncodedContent(startPayload),
            cancellationToken);

        var startBody = await startResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!startResponse.IsSuccessStatusCode)
        {
            return Result.Failure<FacebookPublishResult>(
                new Error("Facebook.ReelStartFailed", ReadGraphApiError(startBody) ?? "Failed to start Facebook Reel upload."));
        }

        var startParsed = JsonSerializer.Deserialize<ReelStartResponse>(startBody, JsonOptions);
        if (string.IsNullOrWhiteSpace(startParsed?.VideoId) || string.IsNullOrWhiteSpace(startParsed?.UploadUrl))
        {
            return Result.Failure<FacebookPublishResult>(
                new Error("Facebook.ReelStartFailed", "Facebook did not return a reel upload url."));
        }

        // Phase 2: hosted-file upload. FB fetches the presigned URL itself.
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, startParsed.UploadUrl);
        uploadRequest.Headers.TryAddWithoutValidation("Authorization", $"OAuth {pageAccessToken}");
        uploadRequest.Headers.TryAddWithoutValidation("file_url", video.Url);
        uploadRequest.Content = new ByteArrayContent(Array.Empty<byte>());

        var uploadResponse = await _httpClient.SendAsync(uploadRequest, cancellationToken);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            return Result.Failure<FacebookPublishResult>(
                new Error("Facebook.ReelUploadFailed", ReadGraphApiError(uploadBody) ?? $"Reel upload failed with status {(int)uploadResponse.StatusCode}: {uploadBody}"));
        }

        // Phase 3: finish / publish
        var finishPayload = new Dictionary<string, string>
        {
            ["upload_phase"] = "finish",
            ["video_id"] = startParsed.VideoId,
            ["video_state"] = "PUBLISHED",
            ["description"] = message ?? string.Empty,
            ["access_token"] = pageAccessToken
        };

        var finishResponse = await _httpClient.PostAsync(
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(pageId)}/video_reels",
            new FormUrlEncodedContent(finishPayload),
            cancellationToken);

        var finishBody = await finishResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!finishResponse.IsSuccessStatusCode)
        {
            return Result.Failure<FacebookPublishResult>(
                new Error("Facebook.ReelPublishFailed", ReadGraphApiError(finishBody) ?? "Failed to publish Facebook Reel."));
        }

        // External id format for reels follows the same {pageId}_{postId} convention used by
        // regular posts — the "postId" here is the video_id, and it's what DELETE/UPDATE use
        // when re-resolving the owning page via /me/accounts.
        var externalId = $"{pageId}_{startParsed.VideoId}";
        return Result.Success(new FacebookPublishResult(pageId, externalId));
    }

    private sealed class ReelStartResponse
    {
        [JsonPropertyName("video_id")]
        public string? VideoId { get; set; }

        [JsonPropertyName("upload_url")]
        public string? UploadUrl { get; set; }
    }

    private async Task<Result<string>> UploadPhotoAsync(
        string pageId,
        string pageAccessToken,
        FacebookPublishMedia image,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, string>
        {
            ["url"] = image.Url,
            ["published"] = "false",
            ["access_token"] = pageAccessToken
        };

        var response = await _httpClient.PostAsync(
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(pageId)}/photos",
            new FormUrlEncodedContent(payload),
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<string>(
                new Error("Facebook.UploadFailed", ReadGraphApiError(body) ?? "Failed to upload photo to Facebook."));
        }

        var parsed = JsonSerializer.Deserialize<GraphApiIdResponse>(body, JsonOptions);
        if (string.IsNullOrWhiteSpace(parsed?.Id))
        {
            return Result.Failure<string>(
                new Error("Facebook.UploadFailed", "Facebook response did not include a media id."));
        }

        return Result.Success(parsed.Id);
    }

    private async Task<Result<string>> PublishFeedAsync(
        string pageId,
        string pageAccessToken,
        string message,
        IReadOnlyList<string> mediaIds,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, string>
        {
            ["message"] = message,
            ["access_token"] = pageAccessToken
        };

        for (var i = 0; i < mediaIds.Count; i++)
        {
            payload[$"attached_media[{i}]"] = JsonSerializer.Serialize(
                new Dictionary<string, string> { ["media_fbid"] = mediaIds[i] },
                JsonOptions);
        }

        var response = await _httpClient.PostAsync(
            $"{GraphApiBaseUrl}/{Uri.EscapeDataString(pageId)}/feed",
            new FormUrlEncodedContent(payload),
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<string>(
                new Error("Facebook.PublishFailed", ReadGraphApiError(body) ?? "Failed to publish Facebook post."));
        }

        var parsed = JsonSerializer.Deserialize<GraphApiIdResponse>(body, JsonOptions);
        if (string.IsNullOrWhiteSpace(parsed?.Id))
        {
            return Result.Failure<string>(
                new Error("Facebook.PublishFailed", "Facebook response did not include a post id."));
        }

        return Result.Success(parsed.Id);
    }

    private async Task<Result<IReadOnlyList<PageAccessInfo>>> ResolvePagesAsync(
        FacebookPublishRequest request,
        CancellationToken cancellationToken)
    {
        // When the SocialMedia row represents a SPECIFIC page (PageId + PageAccessToken are
        // both set), publish ONLY to that page. Fan-out via /me/accounts was causing the
        // same post to land on every page the user token could see — and if the user had
        // linked N per-page SocialMedia rows, each select-and-publish multiplied the result
        // by N (e.g. 2 linked pages × 2 /me/accounts pages = 4 publications per post).
        if (!string.IsNullOrWhiteSpace(request.PageId) && !string.IsNullOrWhiteSpace(request.PageAccessToken))
        {
            return Result.Success<IReadOnlyList<PageAccessInfo>>(
                new[] { new PageAccessInfo(request.PageId!, request.PageAccessToken!) });
        }

        if (string.IsNullOrWhiteSpace(request.UserAccessToken))
        {
            return Result.Failure<IReadOnlyList<PageAccessInfo>>(
                new Error("Facebook.InvalidToken", "User access token is required to fetch pages."));
        }

        var pageResult = await FetchPagesAsync(request.UserAccessToken, cancellationToken);
        if (pageResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<PageAccessInfo>>(pageResult.Error);
        }

        return Result.Success<IReadOnlyList<PageAccessInfo>>(pageResult.Value);
    }

    private async Task<Result<IReadOnlyList<PageAccessInfo>>> FetchPagesAsync(
        string userAccessToken,
        CancellationToken cancellationToken)
    {
        // Coalesce repeated lookups per user token within the TTL window. Each publish /
        // unpublish / delete flow used to hit /me/accounts once per target; with this
        // cache, a 3-target batch calls FB once instead of three times.
        var cacheKey = BuildPageCacheKey(userAccessToken);
        if (_memoryCache.TryGetValue<IReadOnlyList<PageAccessInfo>>(cacheKey, out var cached) && cached is not null)
        {
            return Result.Success(cached);
        }

        var url = $"{GraphApiBaseUrl}/me/accounts?fields=id,name,access_token&access_token={Uri.EscapeDataString(userAccessToken)}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<IReadOnlyList<PageAccessInfo>>(
                new Error("Facebook.PageLookupFailed", ReadGraphApiError(body) ?? "Failed to load Facebook pages."));
        }

        var parsed = JsonSerializer.Deserialize<FacebookPagesResponse>(body, JsonOptions);
        var pages = parsed?.Data
            ?.Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.AccessToken))
            .Select(item => new PageAccessInfo(item.Id!, item.AccessToken!))
            .ToList() ?? new List<PageAccessInfo>();

        if (pages.Count == 0)
        {
            return Result.Failure<IReadOnlyList<PageAccessInfo>>(
                new Error("Facebook.PageNotFound", "No Facebook pages were found for this account."));
        }

        _memoryCache.Set<IReadOnlyList<PageAccessInfo>>(cacheKey, pages, PageListCacheTtl);
        return Result.Success<IReadOnlyList<PageAccessInfo>>(pages);
    }

    private static (List<FacebookPublishMedia> Videos, List<FacebookPublishMedia> Images, List<FacebookPublishMedia> Invalid)
        SplitMedia(IReadOnlyList<FacebookPublishMedia> media)
    {
        var videos = new List<FacebookPublishMedia>();
        var images = new List<FacebookPublishMedia>();
        var invalid = new List<FacebookPublishMedia>();

        foreach (var item in media)
        {
            var mediaType = ResolveMediaType(item);
            switch (mediaType)
            {
                case MediaType.Image:
                    images.Add(item);
                    break;
                case MediaType.Video:
                    videos.Add(item);
                    break;
                default:
                    invalid.Add(item);
                    break;
            }
        }

        return (videos, images, invalid);
    }

    private static MediaType ResolveMediaType(FacebookPublishMedia media)
    {
        if (!string.IsNullOrWhiteSpace(media.ContentType))
        {
            if (media.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return MediaType.Image;
            }

            if (media.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            {
                return MediaType.Video;
            }

            if (string.Equals(media.ContentType, "image", StringComparison.OrdinalIgnoreCase))
            {
                return MediaType.Image;
            }

            if (string.Equals(media.ContentType, "video", StringComparison.OrdinalIgnoreCase))
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

    private sealed record PageAccessInfo(string PageId, string PageAccessToken);

    private sealed class GraphApiIdResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    private sealed class FacebookPagesResponse
    {
        [JsonPropertyName("data")]
        public List<FacebookPage>? Data { get; set; }
    }

    private sealed class FacebookPage
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
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
