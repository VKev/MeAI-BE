using Application.Abstractions.Data;
using Application.Resources.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Resources.Queries;

public sealed record GetSystemStorageSettingsQuery : IRequest<Result<SystemStorageSettingsResponse>>;

public sealed class GetSystemStorageSettingsQueryHandler
    : IRequestHandler<GetSystemStorageSettingsQuery, Result<SystemStorageSettingsResponse>>
{
    private readonly IRepository<Config> _configRepository;
    private readonly IRepository<Resource> _resourceRepository;

    public GetSystemStorageSettingsQueryHandler(IUnitOfWork unitOfWork)
    {
        _configRepository = unitOfWork.Repository<Config>();
        _resourceRepository = unitOfWork.Repository<Resource>();
    }

    public async Task<Result<SystemStorageSettingsResponse>> Handle(
        GetSystemStorageSettingsQuery request,
        CancellationToken cancellationToken)
    {
        var config = await _configRepository.GetAll()
            .AsNoTracking()
            .Where(item => !item.IsDeleted)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var usedBytes = await _resourceRepository.GetAll()
            .AsNoTracking()
            .Where(resource => !resource.IsDeleted)
            .SumAsync(resource => resource.SizeBytes ?? 0L, cancellationToken);

        var resourceCount = await _resourceRepository.GetAll()
            .AsNoTracking()
            .CountAsync(resource => !resource.IsDeleted, cancellationToken);

        var userCount = await _resourceRepository.GetAll()
            .AsNoTracking()
            .Where(resource => !resource.IsDeleted)
            .Select(resource => resource.UserId)
            .Distinct()
            .CountAsync(cancellationToken);

        return Result.Success(BuildResponse(config?.SystemStorageQuotaBytes, usedBytes, resourceCount, userCount, config?.UpdatedAt));
    }

    internal static SystemStorageSettingsResponse BuildResponse(
        long? quotaBytes,
        long usedBytes,
        int resourceCount,
        int userCount,
        DateTime? updatedAt)
    {
        var availableBytes = quotaBytes.HasValue
            ? Math.Max(0L, quotaBytes.Value - usedBytes)
            : (long?)null;

        var usagePercent = quotaBytes is > 0
            ? Math.Round((decimal)usedBytes / quotaBytes.Value * 100m, 2)
            : (decimal?)null;

        return new SystemStorageSettingsResponse(
            quotaBytes,
            quotaBytes.HasValue ? ToGb(quotaBytes.Value) : null,
            usedBytes,
            ToGb(usedBytes),
            availableBytes,
            availableBytes.HasValue ? ToGb(availableBytes.Value) : null,
            usagePercent,
            resourceCount,
            userCount,
            updatedAt);
    }

    private static decimal ToGb(long bytes) =>
        Math.Round(bytes / 1024m / 1024m / 1024m, 2);
}
