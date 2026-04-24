using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Storage;

public interface IObjectStorageService
{
    string? CurrentNamespace { get; }

    Task<Result<StorageUploadResult>> UploadAsync(StorageUploadRequest request, CancellationToken cancellationToken);

    Result<string> GetPresignedUrl(string keyOrUrl, TimeSpan? expiresIn = null);

    Task<Result<bool>> DeleteAsync(string keyOrUrl, CancellationToken cancellationToken);

    Task<Result<bool>> ExistsAsync(string keyOrUrl, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<StorageObjectInfo>>> ListAsync(string? prefix, CancellationToken cancellationToken);
}

public sealed record StorageUploadRequest(
    string Key,
    Stream Content,
    string ContentType,
    long ContentLength);

public sealed record StorageUploadResult(
    string Key,
    string Url,
    string? Bucket = null,
    string? Region = null,
    string? Namespace = null);

public sealed record StorageObjectInfo(
    string Key,
    long SizeBytes,
    DateTime? LastModified);
