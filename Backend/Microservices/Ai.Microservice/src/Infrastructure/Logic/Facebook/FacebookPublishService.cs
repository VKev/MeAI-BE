using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Facebook;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Facebook;

public sealed class FacebookPublishService : IFacebookPublishService
{
    private const string GraphApiBaseUrl = "https://graph.facebook.com/v21.0";
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public FacebookPublishService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("Facebook");
    }

    public async Task<Result<IReadOnlyList<FacebookPublishResult>>> PublishAsync(
        FacebookPublishRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Media.Count == 0)
        {
            return Result.Failure<IReadOnlyList<FacebookPublishResult>>(
                new Error("Facebook.MissingMedia", "At least one media resource is required."));
        }

        var resolveResult = await ResolvePagesAsync(request, cancellationToken);
        if (resolveResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<FacebookPublishResult>>(
                resolveResult.Error);
        }

        var pages = resolveResult.Value;

        var (videos, images, invalidMedia) = SplitMedia(request.Media);

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
            var results = new List<FacebookPublishResult>();
            foreach (var page in pages)
            {
                var publishResult = await PublishVideoAsync(
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
