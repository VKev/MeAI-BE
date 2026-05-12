using System.Net;
using System.Text.RegularExpressions;
using Application.Abstractions.Automation;
using Application.Abstractions.Resources;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.Resources;

namespace Infrastructure.Logic.Automation;

public sealed partial class WebSearchEnrichmentService : IWebSearchEnrichmentService
{
    private const int MaxResultsToFetch = 3;
    private const int MaxPageContentLength = 4000;
    private const int MaxContextExcerptLength = 900;
    private const int MaxMediaUrlsPerResult = 6;
    private const int MaxImportedMediaCount = 6;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUserResourceService _userResourceService;
    private readonly ILogger<WebSearchEnrichmentService> _logger;

    public WebSearchEnrichmentService(
        IHttpClientFactory httpClientFactory,
        IUserResourceService userResourceService,
        ILogger<WebSearchEnrichmentService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _userResourceService = userResourceService;
        _logger = logger;
    }

    public async Task<AgentWebSearchResponse> EnrichAsync(
        AgentWebSearchResponse response,
        Guid? userId,
        Guid? workspaceId,
        Guid? originChatSessionId,
        Guid? originChatId,
        CancellationToken cancellationToken)
    {
        if (response.Results.Count == 0)
        {
            return response with
            {
                LlmContext = BuildLlmContext(response.Query, response.Results),
                ImportedResources = response.ImportedResources ?? []
            };
        }

        var enrichedResults = new List<AgentWebSearchResultItem>(response.Results.Count);
        var importCandidates = new List<MediaImportCandidate>();
        var importDedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < response.Results.Count; index++)
        {
            var result = response.Results[index];
            if (index >= MaxResultsToFetch || !TryCreateHttpUri(result.Url, out var resultUri))
            {
                enrichedResults.Add(result);
                continue;
            }

            var fetched = await TryFetchPageAsync(resultUri, cancellationToken);
            if (fetched is null)
            {
                enrichedResults.Add(result);
                continue;
            }

            var mediaUrls = fetched.MediaUrls
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxMediaUrlsPerResult)
                .ToList();

            if (userId.HasValue)
            {
                foreach (var mediaUrl in mediaUrls)
                {
                    if (importCandidates.Count >= MaxImportedMediaCount)
                    {
                        break;
                    }

                    var normalizedUrl = NormalizeUrl(mediaUrl);
                    if (!importDedup.Add(normalizedUrl))
                    {
                        continue;
                    }

                    var resourceType = ClassifyMediaType(mediaUrl);
                    if (resourceType is null)
                    {
                        continue;
                    }

                    importCandidates.Add(new MediaImportCandidate(mediaUrl, resourceType, result.Url));
                }
            }

            enrichedResults.Add(result with
            {
                PageTitle = string.IsNullOrWhiteSpace(fetched.PageTitle) ? result.Title : fetched.PageTitle,
                PageContent = fetched.PageContent,
                MediaUrls = mediaUrls
            });
        }

        var importedResources = response.ImportedResources?.ToList() ?? [];
        if (userId.HasValue && importCandidates.Count > 0)
        {
            var createdResources = await ImportMediaAsync(
                userId.Value,
                workspaceId,
                originChatSessionId,
                originChatId,
                importCandidates,
                cancellationToken);
            if (createdResources.Count > 0)
            {
                importedResources.AddRange(createdResources);
            }
        }

        return response with
        {
            Results = enrichedResults,
            LlmContext = BuildLlmContext(response.Query, enrichedResults),
            ImportedResources = importedResources
        };
    }

    public async Task<AgentWebSearchResponse> EnrichUrlsAsync(
        IReadOnlyList<string> urls,
        string query,
        Guid? userId,
        Guid? workspaceId,
        Guid? originChatSessionId,
        Guid? originChatId,
        CancellationToken cancellationToken)
    {
        if (urls.Count == 0)
        {
            return new AgentWebSearchResponse(query, DateTime.UtcNow, [], query, []);
        }

        var seedResults = urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Take(MaxResultsToFetch)
            .Select(url => new AgentWebSearchResultItem(
                Title: null,
                Url: url.Trim(),
                Description: null,
                Source: "direct_url"))
            .ToList();

        return await EnrichAsync(
            new AgentWebSearchResponse(
                query,
                DateTime.UtcNow,
                seedResults,
                query,
                []),
            userId,
            workspaceId,
            originChatSessionId,
            originChatId,
            cancellationToken);
    }

    private async Task<PageFetchResult?> TryFetchPageAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("WebSearchContent");
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(contentType))
            {
                contentType = "text/html";
            }

            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return new PageFetchResult(null, null, [uri.ToString()]);
            }

            if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            {
                return new PageFetchResult(null, null, [uri.ToString()]);
            }

            if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase) &&
                !contentType.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            var pageTitle = ExtractPageTitle(html);
            var pageContent = ExtractPageContent(html);
            var mediaUrls = ExtractMediaUrls(html, uri);

            return new PageFetchResult(pageTitle, pageContent, mediaUrls);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Failed to enrich search result URL {Url}", uri);
            return null;
        }
    }

    private async Task<IReadOnlyList<ImportedResourceItem>> ImportMediaAsync(
        Guid userId,
        Guid? workspaceId,
        Guid? originChatSessionId,
        Guid? originChatId,
        IReadOnlyList<MediaImportCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var created = new List<ImportedResourceItem>();

        foreach (var group in candidates.GroupBy(candidate => candidate.ResourceType, StringComparer.OrdinalIgnoreCase))
        {
            var urls = group.Select(candidate => candidate.Url).ToList();
            var uploadResult = await _userResourceService.CreateResourcesFromUrlsAsync(
                userId,
                urls,
                status: "ready",
                resourceType: group.Key,
                cancellationToken,
                workspaceId,
                new ResourceProvenanceMetadata(
                    ResourceOriginKinds.AiImportedUrl,
                    originChatSessionId,
                    originChatId));

            if (uploadResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to import web search media for UserId {UserId}: {Error}",
                    userId,
                    uploadResult.Error.Description);
                continue;
            }

            var sourceCandidates = group.ToList();
            for (var index = 0; index < uploadResult.Value.Count && index < sourceCandidates.Count; index++)
            {
                var resource = uploadResult.Value[index];
                var source = sourceCandidates[index];
                created.Add(new ImportedResourceItem(
                    resource.ResourceId,
                    resource.PresignedUrl,
                    resource.ContentType,
                    resource.ResourceType,
                    source.Url,
                    source.SourcePageUrl));
            }
        }

        return created;
    }

    private static string BuildLlmContext(string query, IReadOnlyList<AgentWebSearchResultItem> results)
    {
        if (results.Count == 0)
        {
            return query;
        }

        return string.Join(
            "\n\n",
            results.Select((item, index) =>
            {
                var excerpt = Truncate(item.PageContent ?? item.Description, MaxContextExcerptLength);
                return string.Join(
                    "\n",
                    new[]
                    {
                        $"{index + 1}. {item.PageTitle ?? item.Title ?? "Untitled"}",
                        item.Url,
                        item.Description,
                        excerpt
                    }.Where(value => !string.IsNullOrWhiteSpace(value)));
            }));
    }

    private static string? ExtractPageTitle(string html)
    {
        return FirstNonEmpty(
            ExtractMetaContent(html, "property", "og:title"),
            ExtractMetaContent(html, "name", "twitter:title"),
            ExtractTagInnerText(html, "title"));
    }

    private static string? ExtractPageContent(string html)
    {
        var withoutScripts = ScriptRegex().Replace(html, " ");
        var withoutStyles = StyleRegex().Replace(withoutScripts, " ");
        var withoutNoscript = NoscriptRegex().Replace(withoutStyles, " ");
        var withoutComments = HtmlCommentRegex().Replace(withoutNoscript, " ");
        var withLineBreaks = BlockBoundaryRegex().Replace(withoutComments, "\n");
        var text = HtmlTagRegex().Replace(withLineBreaks, " ");
        text = WebUtility.HtmlDecode(text);
        text = WhitespaceRegex().Replace(text, " ").Trim();

        return string.IsNullOrWhiteSpace(text)
            ? null
            : Truncate(text, MaxPageContentLength);
    }

    private static List<string> ExtractMediaUrls(string html, Uri baseUri)
    {
        var urls = new List<string>();

        AddMediaUrls(urls, ExtractMetaContent(html, "property", "og:image"), baseUri);
        AddMediaUrls(urls, ExtractMetaContent(html, "name", "twitter:image"), baseUri);
        AddMediaUrls(urls, ExtractMetaContent(html, "property", "og:video"), baseUri);
        AddMediaUrls(urls, ExtractAttributeValues(html, "img", "src"), baseUri);
        AddMediaUrls(urls, ExtractAttributeValues(html, "img", "data-src"), baseUri);
        AddMediaUrls(urls, ExtractAttributeValues(html, "video", "src"), baseUri);
        AddMediaUrls(urls, ExtractAttributeValues(html, "video", "poster"), baseUri);
        AddMediaUrls(urls, ExtractAttributeValues(html, "source", "src"), baseUri);
        AddMediaUrls(urls, ExtractAttributeValues(html, "a", "href"), baseUri);
        AddMediaUrls(urls, ExtractSrcSetValues(html), baseUri);

        return urls
            .Select(NormalizeUrl)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Where(value => ClassifyMediaType(value) is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddMediaUrls(List<string> urls, string? value, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        AddMediaUrls(urls, [value], baseUri);
    }

    private static void AddMediaUrls(List<string> urls, IEnumerable<string> values, Uri baseUri)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!Uri.TryCreate(baseUri, value.Trim(), out var resolved))
            {
                continue;
            }

            if (!string.Equals(resolved.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(resolved.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            urls.Add(resolved.ToString());
        }
    }

    private static IEnumerable<string> ExtractAttributeValues(string html, string tagName, string attributeName)
    {
        var pattern = $@"(?is)<{Regex.Escape(tagName)}\b[^>]*\b{Regex.Escape(attributeName)}\s*=\s*[""'](?<value>[^""']+)[""']";
        return Regex.Matches(html, pattern)
            .Select(match => match.Groups["value"].Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value));
    }

    private static IEnumerable<string> ExtractSrcSetValues(string html)
    {
        foreach (var srcset in ExtractAttributeValues(html, "img", "srcset"))
        {
            foreach (var candidate in srcset.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var value = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
        }
    }

    private static string? ExtractMetaContent(string html, string attributeName, string attributeValue)
    {
        var pattern = $@"(?is)<meta\b[^>]*\b{Regex.Escape(attributeName)}\s*=\s*[""']{Regex.Escape(attributeValue)}[""'][^>]*\bcontent\s*=\s*[""'](?<value>[^""']+)[""']";
        var match = Regex.Match(html, pattern);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static string? ExtractTagInnerText(string html, string tagName)
    {
        var pattern = $@"(?is)<{Regex.Escape(tagName)}\b[^>]*>(?<value>.*?)</{Regex.Escape(tagName)}>";
        var match = Regex.Match(html, pattern);
        if (!match.Success)
        {
            return null;
        }

        return WebUtility.HtmlDecode(HtmlTagRegex().Replace(match.Groups["value"].Value, " ")).Trim();
    }

    private static bool TryCreateHttpUri(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out uri!) &&
            (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        uri = null!;
        return false;
    }

    private static string NormalizeUrl(string value)
    {
        return value.Trim();
    }

    private static string? ClassifyMediaType(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var path = url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            path = uri.AbsolutePath;
        }

        var extension = Path.GetExtension(path).Trim().ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp" or ".svg" or ".avif" => "image",
            ".mp4" or ".mov" or ".webm" or ".m4v" or ".avi" or ".mkv" or ".mpeg" or ".mpg" => "video",
            _ when url.Contains("/image", StringComparison.OrdinalIgnoreCase) => "image",
            _ when url.Contains("/video", StringComparison.OrdinalIgnoreCase) => "video",
            _ => null
        };
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength].TrimEnd() + "...";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    [GeneratedRegex("(?is)<script\\b[^>]*>.*?</script>")]
    private static partial Regex ScriptRegex();

    [GeneratedRegex("(?is)<style\\b[^>]*>.*?</style>")]
    private static partial Regex StyleRegex();

    [GeneratedRegex("(?is)<noscript\\b[^>]*>.*?</noscript>")]
    private static partial Regex NoscriptRegex();

    [GeneratedRegex("(?is)<!--.*?-->")]
    private static partial Regex HtmlCommentRegex();

    [GeneratedRegex("(?is)</?(p|div|section|article|main|aside|header|footer|li|ul|ol|h1|h2|h3|h4|h5|h6|br|tr|td|th)\\b[^>]*>")]
    private static partial Regex BlockBoundaryRegex();

    [GeneratedRegex("(?is)<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();

    private sealed record PageFetchResult(
        string? PageTitle,
        string? PageContent,
        IReadOnlyList<string> MediaUrls);

    private sealed record MediaImportCandidate(
        string Url,
        string ResourceType,
        string? SourcePageUrl);
}
