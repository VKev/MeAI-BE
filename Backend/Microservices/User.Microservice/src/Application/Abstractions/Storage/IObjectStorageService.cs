using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Storage;

public interface IObjectStorageService
{
    Task<Result<StorageUploadResult>> UploadAsync(StorageUploadRequest request, CancellationToken cancellationToken);

    Result<string> GetPresignedUrl(string keyOrUrl, TimeSpan? expiresIn = null);

    Task<Result<bool>> DeleteAsync(string keyOrUrl, CancellationToken cancellationToken);
}

public sealed record StorageUploadRequest(
    string Key,
    Stream Content,
    string ContentType,
    long ContentLength);

public sealed record StorageUploadResult(string Key, string Url);
