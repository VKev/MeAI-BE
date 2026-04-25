using Application.Abstractions.Data;
using Application.Resources.Models;
using Application.Subscriptions.Services;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Resources.Services;

public sealed class StorageUsageService : IStorageUsageService
{
    private readonly IRepository<Config> _configRepository;
    private readonly IRepository<Resource> _resourceRepository;
    private readonly IUserSubscriptionEntitlementService _entitlementService;

    public StorageUsageService(
        IUnitOfWork unitOfWork,
        IUserSubscriptionEntitlementService entitlementService)
    {
        _configRepository = unitOfWork.Repository<Config>();
        _resourceRepository = unitOfWork.Repository<Resource>();
        _entitlementService = entitlementService;
    }

    public async Task<Result<StorageUsageResponse>> GetUserUsageAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var usage = await BuildUsageAsync(userId, cancellationToken);
        return Result.Success(usage);
    }

    public async Task<Result<StorageUsageResponse>> EnsureUploadAllowedAsync(
        Guid userId,
        long requestedBytes,
        CancellationToken cancellationToken)
    {
        if (requestedBytes < 0)
        {
            requestedBytes = 0;
        }

        var usage = await BuildUsageAsync(userId, cancellationToken);

        if (usage.MaxUploadFileBytes.HasValue && requestedBytes > usage.MaxUploadFileBytes.Value)
        {
            return Result.Failure<StorageUsageResponse>(
                new Error(
                    "Resource.FileTooLarge",
                    "File size exceeds the current plan upload limit.",
                    new Dictionary<string, object?>
                    {
                        ["maxUploadFileBytes"] = usage.MaxUploadFileBytes.Value,
                        ["requestedBytes"] = requestedBytes
                    }));
        }

        if (usage.QuotaBytes.HasValue && usage.UsedBytes + usage.ReservedBytes + requestedBytes > usage.QuotaBytes.Value)
        {
            var availableBytes = Math.Max(0L, usage.QuotaBytes.Value - usage.UsedBytes - usage.ReservedBytes);
            return Result.Failure<StorageUsageResponse>(
                new Error(
                    "Resource.StorageQuotaExceeded",
                    "Storage quota exceeded.",
                    new Dictionary<string, object?>
                    {
                        ["quotaBytes"] = usage.QuotaBytes.Value,
                        ["usedBytes"] = usage.UsedBytes,
                        ["reservedBytes"] = usage.ReservedBytes,
                        ["requestedBytes"] = requestedBytes,
                        ["availableBytes"] = availableBytes
                    }));
        }

        var systemQuotaBytes = await GetSystemStorageQuotaBytesAsync(cancellationToken);
        if (systemQuotaBytes.HasValue)
        {
            var systemUsedBytes = await _resourceRepository.GetAll()
                .AsNoTracking()
                .Where(resource => !resource.IsDeleted)
                .SumAsync(resource => resource.SizeBytes ?? 0L, cancellationToken);

            if (systemUsedBytes + requestedBytes > systemQuotaBytes.Value)
            {
                return Result.Failure<StorageUsageResponse>(
                    new Error(
                        "Resource.SystemStorageQuotaExceeded",
                        "System storage quota exceeded.",
                        new Dictionary<string, object?>
                        {
                            ["systemStorageQuotaBytes"] = systemQuotaBytes.Value,
                            ["usedBytes"] = systemUsedBytes,
                            ["requestedBytes"] = requestedBytes,
                            ["availableBytes"] = Math.Max(0L, systemQuotaBytes.Value - systemUsedBytes)
                        }));
            }
        }

        return Result.Success(usage);
    }

    private async Task<StorageUsageResponse> BuildUsageAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entitlement = await _entitlementService.GetCurrentEntitlementAsync(userId, cancellationToken);
        var freeQuotaBytes = await GetFreeStorageQuotaBytesAsync(cancellationToken);
        var quotaBytes = entitlement.StorageQuotaBytes(freeQuotaBytes);
        var usedBytes = await _resourceRepository.GetAll()
            .AsNoTracking()
            .Where(resource => resource.UserId == userId && !resource.IsDeleted)
            .SumAsync(resource => resource.SizeBytes ?? 0L, cancellationToken);

        long? availableBytes = quotaBytes.HasValue
            ? Math.Max(0L, quotaBytes.Value - usedBytes)
            : null;
        decimal? usagePercent = quotaBytes is > 0
            ? Math.Round((decimal)usedBytes / quotaBytes.Value * 100m, 2)
            : null;

        return new StorageUsageResponse(
            userId,
            entitlement.CurrentSubscription?.SubscriptionId,
            entitlement.CurrentPlan?.Name,
            quotaBytes,
            usedBytes,
            0,
            availableBytes,
            usagePercent,
            entitlement.MaxUploadFileBytes,
            quotaBytes.HasValue && usedBytes > quotaBytes.Value);
    }

    private async Task<long?> GetFreeStorageQuotaBytesAsync(CancellationToken cancellationToken)
    {
        var config = await _configRepository.GetAll()
            .AsNoTracking()
            .Where(item => !item.IsDeleted)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return config?.FreeStorageQuotaBytes ?? UserSubscriptionEntitlement.DefaultFreeStorageQuotaBytes;
    }

    private async Task<long?> GetSystemStorageQuotaBytesAsync(CancellationToken cancellationToken)
    {
        var config = await _configRepository.GetAll()
            .AsNoTracking()
            .Where(item => !item.IsDeleted)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return config?.SystemStorageQuotaBytes;
    }
}
