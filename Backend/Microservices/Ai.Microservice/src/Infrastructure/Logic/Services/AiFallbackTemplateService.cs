using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Infrastructure.Logic.Services;

public interface IAiFallbackTemplateService
{
    bool TryGetImageFallback(out AiFallbackAsset asset);
    bool TryGetVideoFallback(out AiFallbackAsset asset);
}

public sealed record AiFallbackAsset(
    string ResultUrl,
    string ContentType);

internal sealed record AiFallbackTemplate(
    string ContentType,
    string FileName,
    string FilePath);

public sealed class AiFallbackTemplateService : IAiFallbackTemplateService
{
    private const string ImageType = "image";
    private const string VideoType = "video";

    private readonly ILogger<AiFallbackTemplateService> _logger;
    private readonly string _templateDirectoryPath;
    private readonly ConcurrentDictionary<string, string> _dataUrlCache = new();

    public AiFallbackTemplateService(
        IConfiguration configuration,
        ILogger<AiFallbackTemplateService> logger)
    {
        _logger = logger;
        _templateDirectoryPath =
            configuration["AiFallback:TemplateDirectory"] ??
            configuration["AiFallback__TemplateDirectory"] ??
            Path.Combine(AppContext.BaseDirectory, "AIFallbackTemplates");
    }

    public bool TryGetImageFallback(out AiFallbackAsset asset)
        => TryGetFallback(ImageType, out asset);

    public bool TryGetVideoFallback(out AiFallbackAsset asset)
        => TryGetFallback(VideoType, out asset);

    private bool TryGetTemplate(string mediaType, out AiFallbackTemplate template)
    {
        var normalizedType = mediaType.Trim().ToLowerInvariant();

        var (fileName, contentType) = normalizedType switch
        {
            ImageType => ("image.webp", "image/webp"),
            VideoType => ("video.mp4", "video/mp4"),
            _ => (string.Empty, string.Empty)
        };

        if (string.IsNullOrWhiteSpace(fileName))
        {
            template = default!;
            return false;
        }

        var filePath = Path.Combine(
            _templateDirectoryPath,
            fileName);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning(
                "Fallback template file is missing for media type {MediaType}. Expected path: {FilePath}",
                normalizedType,
                filePath);

            template = default!;
            return false;
        }

        template = new AiFallbackTemplate(
            ContentType: contentType,
            FileName: fileName,
            FilePath: filePath);

        return true;
    }

    private bool TryGetFallback(
        string mediaType,
        out AiFallbackAsset asset)
    {
        if (!TryGetTemplate(mediaType, out var template))
        {
            asset = default!;
            return false;
        }

        string dataUrl;
        try
        {
            dataUrl = _dataUrlCache.GetOrAdd(template.FilePath, path =>
            {
                var fileBytes = File.ReadAllBytes(path);
                return $"data:{template.ContentType};base64,{Convert.ToBase64String(fileBytes)}";
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to read fallback template for media type {MediaType} at path {FilePath}",
                mediaType,
                template.FilePath);

            asset = default!;
            return false;
        }

        asset = new AiFallbackAsset(
            ResultUrl: dataUrl,
            ContentType: template.ContentType);

        return true;
    }
}
