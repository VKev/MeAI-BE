using Application.Resources.Models;
using SharedLibrary.Common.ResponseModel;

namespace Application.Resources.Services;

public interface IStorageUsageService
{
    Task<Result<StorageUsageResponse>> GetUserUsageAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<Result<StorageUsageResponse>> EnsureUploadAllowedAsync(
        Guid userId,
        long requestedBytes,
        CancellationToken cancellationToken);
}
