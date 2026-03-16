using System.Net.Http.Headers;
using System.Text;
using Application.Abstractions.Storage;
using SharedLibrary.Common.ResponseModel;

namespace Infrastructure.Logic.Storage;

public sealed class RemoteFileService : IRemoteFileService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public RemoteFileService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<Result<RemoteFileResult>> FetchAsync(string url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Result.Failure<RemoteFileResult>(new Error("Resource.InvalidUrl", "Resource URL is required."));
        }

        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseDataUrl(url);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return Result.Failure<RemoteFileResult>(new Error("Resource.InvalidUrl", "Resource URL is invalid."));
        }

        var client = _httpClientFactory.CreateClient("ResourceFetch");
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<RemoteFileResult>(
                new Error("Resource.FetchFailed", $"Failed to fetch remote resource. Status {(int)response.StatusCode}."));
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (string.IsNullOrWhiteSpace(contentType))
        {
            contentType = "application/octet-stream";
        }

        var fileName = GetFileName(response.Content.Headers.ContentDisposition, uri);
        var bufferedStream = new MemoryStream();
        await response.Content.CopyToAsync(bufferedStream, cancellationToken);
        bufferedStream.Position = 0;

        return Result.Success(new RemoteFileResult(
            bufferedStream,
            fileName,
            contentType,
            bufferedStream.Length));
    }

    private static Result<RemoteFileResult> TryParseDataUrl(string dataUrl)
    {
        const string dataPrefix = "data:";
        var commaIndex = dataUrl.IndexOf(',');
        if (commaIndex <= dataPrefix.Length)
        {
            return Result.Failure<RemoteFileResult>(
                new Error("Resource.InvalidUrl", "Data URL is invalid."));
        }

        var metadata = dataUrl[dataPrefix.Length..commaIndex];
        var encodedContent = dataUrl[(commaIndex + 1)..];

        var isBase64 = metadata.EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
        if (isBase64)
        {
            metadata = metadata[..^7];
        }

        var contentType = string.IsNullOrWhiteSpace(metadata)
            ? "application/octet-stream"
            : metadata.Trim();

        byte[] contentBytes;
        try
        {
            contentBytes = isBase64
                ? Convert.FromBase64String(encodedContent)
                : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(encodedContent));
        }
        catch (FormatException)
        {
            return Result.Failure<RemoteFileResult>(
                new Error("Resource.InvalidUrl", "Data URL content is invalid."));
        }

        var stream = new MemoryStream(contentBytes);
        var fileName = $"resource-{Guid.NewGuid():N}{GuessExtension(contentType)}";

        return Result.Success(new RemoteFileResult(
            stream,
            fileName,
            contentType,
            stream.Length));
    }

    private static string GetFileName(ContentDispositionHeaderValue? contentDisposition, Uri uri)
    {
        var fromHeader = contentDisposition?.FileNameStar ?? contentDisposition?.FileName;
        if (!string.IsNullOrWhiteSpace(fromHeader))
        {
            return fromHeader.Trim('"');
        }

        var fileName = Path.GetFileName(uri.AbsolutePath);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            return fileName;
        }

        return $"resource-{Guid.NewGuid():N}";
    }

    private static string GuessExtension(string contentType)
    {
        var slashIndex = contentType.IndexOf('/');
        if (slashIndex < 0 || slashIndex >= contentType.Length - 1)
        {
            return ".bin";
        }

        var subtype = contentType[(slashIndex + 1)..];
        var semicolonIndex = subtype.IndexOf(';');
        if (semicolonIndex >= 0)
        {
            subtype = subtype[..semicolonIndex];
        }

        subtype = subtype.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(subtype))
        {
            return ".bin";
        }

        var sanitizedSubtype = new string(
            subtype.Where(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_').ToArray());

        return string.IsNullOrWhiteSpace(sanitizedSubtype)
            ? ".bin"
            : $".{sanitizedSubtype}";
    }
}
