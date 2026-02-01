using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Storage;

public interface IRemoteFileService
{
    Task<Result<RemoteFileResult>> FetchAsync(string url, CancellationToken cancellationToken);
}

public sealed record RemoteFileResult(
    Stream Content,
    string FileName,
    string ContentType,
    long ContentLength);
