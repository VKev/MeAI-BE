using Application.Resources.Services;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Resources.Queries;

public sealed record CheckStorageQuotaQuery(
    Guid UserId,
    long RequestedBytes,
    string? Purpose,
    int EstimatedFileCount,
    Guid? WorkspaceId) : IRequest<Result<StorageQuotaCheckResponse>>;

public sealed record StorageQuotaCheckResponse(
    bool Allowed,
    long? QuotaBytes,
    long UsedBytes,
    long ReservedBytes,
    long? AvailableBytes,
    long? MaxUploadFileBytes,
    long? SystemStorageQuotaBytes,
    Error? Error);

public sealed class CheckStorageQuotaQueryHandler
    : IRequestHandler<CheckStorageQuotaQuery, Result<StorageQuotaCheckResponse>>
{
#pragma warning disable CS0169
    private readonly User? _domainDependency;
#pragma warning restore CS0169
    private readonly IStorageUsageService _storageUsageService;

    public CheckStorageQuotaQueryHandler(IStorageUsageService storageUsageService)
    {
        _storageUsageService = storageUsageService;
    }

    public async Task<Result<StorageQuotaCheckResponse>> Handle(
        CheckStorageQuotaQuery request,
        CancellationToken cancellationToken)
    {
        var (usage, error) = await _storageUsageService.CheckQuotaAsync(
            request.UserId,
            request.RequestedBytes,
            cancellationToken);

        return Result.Success(new StorageQuotaCheckResponse(
            error is null,
            usage.QuotaBytes,
            usage.UsedBytes,
            usage.ReservedBytes,
            usage.AvailableBytes,
            usage.MaxUploadFileBytes,
            usage.SystemStorageQuotaBytes,
            error));
    }
}
