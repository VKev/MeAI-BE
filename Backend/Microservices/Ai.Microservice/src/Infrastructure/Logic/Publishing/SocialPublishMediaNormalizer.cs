using Application.Abstractions.Publishing;
using Application.Abstractions.Resources;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.Resources;
using SharedLibrary.Common.ResponseModel;
using SkiaSharp;

namespace Infrastructure.Logic.Publishing;

public sealed class SocialPublishMediaNormalizer : ISocialPublishMediaNormalizer
{
    private const string FacebookType = "facebook";
    private const string InstagramType = "instagram";
    private const string ThreadsType = "threads";
    private const string TikTokType = "tiktok";
    private const string ReelsType = "reels";
    private const int MaxSourceImageBytes = 40 * 1024 * 1024;
    private const int MaxJpegBytes = 20 * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly IUserResourceService _userResourceService;
    private readonly ISocialPublishVideoTranscoder _videoTranscoder;
    private readonly ILogger<SocialPublishMediaNormalizer> _logger;

    public SocialPublishMediaNormalizer(
        IHttpClientFactory httpClientFactory,
        IUserResourceService userResourceService,
        ISocialPublishVideoTranscoder videoTranscoder,
        ILogger<SocialPublishMediaNormalizer> logger)
    {
        _httpClient = httpClientFactory.CreateClient("SocialPublishMedia");
        _userResourceService = userResourceService;
        _videoTranscoder = videoTranscoder;
        _logger = logger;
    }

    public async Task<Result<SocialPublishMediaNormalizationResult>> NormalizeAsync(
        Guid userId,
        Guid? workspaceId,
        string platform,
        string? postType,
        IReadOnlyList<UserResourcePresignResult> resources,
        CancellationToken cancellationToken)
    {
        if (resources.Count == 0)
        {
            return Result.Success(new SocialPublishMediaNormalizationResult(resources, Array.Empty<Guid>()));
        }

        var normalizedPlatform = platform.Trim().ToLowerInvariant();
        var normalizedPostType = NormalizePostType(postType);
        var normalizedResources = new List<UserResourcePresignResult>(resources.Count);
        var temporaryResourceIds = new List<Guid>();

        foreach (var resource in resources)
        {
            if (ShouldConvertImage(normalizedPlatform, normalizedPostType, resource))
            {
                var jpegResult = await ConvertToJpegAsync(
                    resource,
                    resizeForTikTok: string.Equals(normalizedPlatform, TikTokType, StringComparison.Ordinal),
                    cancellationToken);

                if (jpegResult.IsFailure)
                {
                    await DeleteTemporaryResourcesBestEffortAsync(userId, temporaryResourceIds);
                    return Result.Failure<SocialPublishMediaNormalizationResult>(jpegResult.Error);
                }

                var uploadResult = await StoreDerivativeAsync(
                    userId,
                    workspaceId,
                    jpegResult.Value,
                    "image/jpeg",
                    "image",
                    cancellationToken);

                if (uploadResult.IsFailure)
                {
                    await DeleteTemporaryResourcesBestEffortAsync(userId, temporaryResourceIds);
                    return Result.Failure<SocialPublishMediaNormalizationResult>(uploadResult.Error);
                }

                AddDerivative(normalizedResources, temporaryResourceIds, uploadResult.Value);
                _logger.LogInformation(
                    "Created temporary social publish image derivative. Platform: {Platform}, SourceResourceId: {SourceResourceId}, DerivativeResourceId: {DerivativeResourceId}",
                    normalizedPlatform,
                    resource.ResourceId,
                    uploadResult.Value.ResourceId);
                continue;
            }

            if (ShouldConvertVideo(normalizedPlatform, resource))
            {
                var mp4Result = await _videoTranscoder.ConvertToMp4Async(resource, cancellationToken);
                if (mp4Result.IsFailure)
                {
                    await DeleteTemporaryResourcesBestEffortAsync(userId, temporaryResourceIds);
                    return Result.Failure<SocialPublishMediaNormalizationResult>(mp4Result.Error);
                }

                var uploadResult = await StoreDerivativeAsync(
                    userId,
                    workspaceId,
                    mp4Result.Value,
                    "video/mp4",
                    "video",
                    cancellationToken);

                if (uploadResult.IsFailure)
                {
                    await DeleteTemporaryResourcesBestEffortAsync(userId, temporaryResourceIds);
                    return Result.Failure<SocialPublishMediaNormalizationResult>(uploadResult.Error);
                }

                AddDerivative(normalizedResources, temporaryResourceIds, uploadResult.Value);
                _logger.LogInformation(
                    "Created temporary social publish video derivative. Platform: {Platform}, SourceResourceId: {SourceResourceId}, DerivativeResourceId: {DerivativeResourceId}",
                    normalizedPlatform,
                    resource.ResourceId,
                    uploadResult.Value.ResourceId);
                continue;
            }

            normalizedResources.Add(resource);
        }

        return Result.Success(new SocialPublishMediaNormalizationResult(
            normalizedResources,
            temporaryResourceIds));
    }

    private async Task<Result<UserResourceCreatedResult>> StoreDerivativeAsync(
        Guid userId,
        Guid? workspaceId,
        byte[] content,
        string contentType,
        string resourceType,
        CancellationToken cancellationToken)
    {
        var dataUrl = $"data:{contentType};base64,{Convert.ToBase64String(content)}";
        var uploadResult = await _userResourceService.CreateResourcesFromUrlsAsync(
            userId,
            new[] { dataUrl },
            status: "social_publish_derivative",
            resourceType,
            cancellationToken,
            workspaceId,
            new ResourceProvenanceMetadata("social_publish_conversion"));

        if (uploadResult.IsFailure)
        {
            return Result.Failure<UserResourceCreatedResult>(uploadResult.Error);
        }

        if (uploadResult.Value.Count == 0)
        {
            return Result.Failure<UserResourceCreatedResult>(
                new Error(
                    "SocialPublish.MediaConversionUploadFailed",
                    "Converted social publish media could not be stored."));
        }

        return Result.Success(uploadResult.Value[0]);
    }

    private static void AddDerivative(
        ICollection<UserResourcePresignResult> normalizedResources,
        ICollection<Guid> temporaryResourceIds,
        UserResourceCreatedResult uploaded)
    {
        temporaryResourceIds.Add(uploaded.ResourceId);
        normalizedResources.Add(new UserResourcePresignResult(
            uploaded.ResourceId,
            uploaded.PresignedUrl,
            uploaded.ContentType,
            uploaded.ResourceType,
            uploaded.OriginKind,
            uploaded.OriginSourceUrl,
            uploaded.OriginChatSessionId,
            uploaded.OriginChatId));
    }

    private async Task<Result<byte[]>> ConvertToJpegAsync(
        UserResourcePresignResult resource,
        bool resizeForTikTok,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                resource.PresignedUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<byte[]>(
                    new Error("SocialPublish.ImageConversionFetchFailed",
                        $"Could not fetch image for social publish conversion. Status {(int)response.StatusCode}."));
            }

            if (response.Content.Headers.ContentLength > MaxSourceImageBytes)
            {
                return Result.Failure<byte[]>(
                    new Error("SocialPublish.ImageConversionTooLarge",
                        "Image is too large to convert for social publishing."));
            }

            await using var source = new MemoryStream();
            var copyResult = await CopyBoundedAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                source,
                MaxSourceImageBytes,
                cancellationToken);

            if (copyResult.IsFailure)
            {
                return Result.Failure<byte[]>(copyResult.Error);
            }

            source.Position = 0;
            using var bitmap = SKBitmap.Decode(source);
            if (bitmap == null)
            {
                return Result.Failure<byte[]>(
                    new Error("SocialPublish.ImageConversionFailed",
                        "Could not decode image for social publishing."));
            }

            var targetSize = resizeForTikTok
                ? GetBoundedTikTokImageSize(bitmap.Width, bitmap.Height)
                : (Width: bitmap.Width, Height: bitmap.Height);
            var imageInfo = new SKImageInfo(
                targetSize.Width,
                targetSize.Height,
                SKColorType.Rgba8888,
                SKAlphaType.Premul);
            using var surface = SKSurface.Create(imageInfo);
            surface.Canvas.Clear(SKColors.White);
            using var paint = new SKPaint { IsAntialias = true };
            surface.Canvas.DrawBitmap(
                bitmap,
                new SKRect(0, 0, targetSize.Width, targetSize.Height),
                paint);
            using var image = surface.Snapshot();

            foreach (var quality in new[] { 90, 82, 74 })
            {
                using var output = image.Encode(SKEncodedImageFormat.Jpeg, quality);
                if (output.Size <= MaxJpegBytes)
                {
                    return Result.Success(output.ToArray());
                }
            }

            return Result.Failure<byte[]>(
                new Error("SocialPublish.ImageConversionTooLarge",
                    "Converted image exceeds the 20 MB social publishing limit."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Social publish image conversion failed. ResourceId: {ResourceId}",
                resource.ResourceId);
            return Result.Failure<byte[]>(
                new Error("SocialPublish.ImageConversionFailed",
                    $"Could not convert image to a supported JPEG format: {ex.Message}"));
        }
    }

    private async Task DeleteTemporaryResourcesBestEffortAsync(
        Guid userId,
        IReadOnlyCollection<Guid> temporaryResourceIds)
    {
        if (temporaryResourceIds.Count == 0)
        {
            return;
        }

        var deleteResult = await _userResourceService.DeleteResourcesAsync(
            userId,
            temporaryResourceIds,
            hardDelete: true,
            CancellationToken.None);

        if (deleteResult.IsFailure)
        {
            _logger.LogWarning(
                "Failed to clean up temporary social publish image derivatives. UserId: {UserId}, Error: {Error}",
                userId,
                deleteResult.Error.Description);
        }
    }

    private static async Task<Result<bool>> CopyBoundedAsync(
        Stream source,
        Stream destination,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long totalBytes = 0;

        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                return Result.Success(true);
            }

            totalBytes += bytesRead;
            if (totalBytes > maxBytes)
            {
                return Result.Failure<bool>(
                    new Error("SocialPublish.ImageConversionTooLarge",
                        "Image is too large to convert for social publishing."));
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }
    }

    private static bool ShouldConvertImage(
        string platform,
        string postType,
        UserResourcePresignResult resource)
    {
        if (!IsImageResource(resource))
        {
            return false;
        }

        if (string.Equals(platform, TikTokType, StringComparison.Ordinal))
        {
            return !string.Equals(postType, ReelsType, StringComparison.Ordinal);
        }

        return platform switch
        {
            InstagramType => !HasCompatibleImageFormat(resource, "image/jpeg", "image/jpg", "image/png"),
            ThreadsType => !HasCompatibleImageFormat(resource, "image/jpeg", "image/jpg", "image/png"),
            FacebookType => !HasCompatibleImageFormat(resource, "image/jpeg", "image/jpg", "image/png", "image/gif"),
            _ => false
        };
    }

    private static bool ShouldConvertVideo(
        string platform,
        UserResourcePresignResult resource)
    {
        return IsVideoResource(resource) &&
               platform is FacebookType or InstagramType or ThreadsType or TikTokType;
    }

    private static bool HasCompatibleImageFormat(
        UserResourcePresignResult resource,
        params string[] allowedContentTypes)
    {
        if (!string.IsNullOrWhiteSpace(resource.ContentType) &&
            !string.Equals(resource.ContentType, "image", StringComparison.OrdinalIgnoreCase))
        {
            return allowedContentTypes.Contains(resource.ContentType, StringComparer.OrdinalIgnoreCase);
        }

        var extension = GetUrlExtension(resource.PresignedUrl);
        return extension switch
        {
            ".jpg" or ".jpeg" => allowedContentTypes.Contains("image/jpeg", StringComparer.OrdinalIgnoreCase),
            ".png" => allowedContentTypes.Contains("image/png", StringComparer.OrdinalIgnoreCase),
            ".gif" => allowedContentTypes.Contains("image/gif", StringComparer.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool IsImageResource(UserResourcePresignResult resource)
    {
        if (!string.IsNullOrWhiteSpace(resource.ContentType) &&
            (resource.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(resource.ContentType, "image", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (string.Equals(resource.ResourceType, "image", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return GetUrlExtension(resource.PresignedUrl) is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp";
    }

    private static bool IsVideoResource(UserResourcePresignResult resource)
    {
        if (!string.IsNullOrWhiteSpace(resource.ContentType) &&
            (resource.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(resource.ContentType, "video", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (string.Equals(resource.ResourceType, "video", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return GetUrlExtension(resource.PresignedUrl) is ".mp4" or ".mov" or ".m4v" or ".webm";
    }

    private static string NormalizePostType(string? postType)
    {
        var normalized = postType?.Trim().ToLowerInvariant();
        return normalized is "reel" or "reels" or "video" ? ReelsType : "posts";
    }

    private static string GetUrlExtension(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var queryIndex = url.IndexOf('?', StringComparison.Ordinal);
        var cleanUrl = queryIndex > 0 ? url[..queryIndex] : url;
        return Path.GetExtension(cleanUrl).ToLowerInvariant();
    }

    private static (int Width, int Height) GetBoundedTikTokImageSize(int width, int height)
    {
        var maxWidth = width >= height ? 1920 : 1080;
        var maxHeight = width >= height ? 1080 : 1920;
        var scale = Math.Min(1d, Math.Min(maxWidth / (double)width, maxHeight / (double)height));

        return (
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }
}
