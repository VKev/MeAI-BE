using System.Net.Http.Headers;
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
}
