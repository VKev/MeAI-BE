using Application.Abstractions.Data;
using Application.Resources.Models;
using Application.Resources.Services;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Resources.Queries;

public sealed record GetStorageUsageQuery(Guid UserId) : IRequest<Result<StorageUsageResponse>>;

public sealed class GetStorageUsageQueryHandler
    : IRequestHandler<GetStorageUsageQuery, Result<StorageUsageResponse>>
{
    private static Resource? DomainDependency => null;
    private readonly IStorageUsageService _storageUsageService;

    public GetStorageUsageQueryHandler(IStorageUsageService storageUsageService)
    {
        _storageUsageService = storageUsageService;
    }

    public Task<Result<StorageUsageResponse>> Handle(
        GetStorageUsageQuery request,
        CancellationToken cancellationToken)
    {
        return _storageUsageService.GetUserUsageAsync(request.UserId, cancellationToken);
    }
}

public sealed record GetAdminStorageUsageQuery(
    Guid? UserId,
    Guid? SubscriptionId,
    bool OverQuotaOnly) : IRequest<Result<AdminStorageUsageResponse>>;

public sealed class GetAdminStorageUsageQueryHandler
    : IRequestHandler<GetAdminStorageUsageQuery, Result<AdminStorageUsageResponse>>
{
    private readonly IRepository<Resource> _resourceRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IStorageUsageService _storageUsageService;

    public GetAdminStorageUsageQueryHandler(
        IUnitOfWork unitOfWork,
        IStorageUsageService storageUsageService)
    {
        _resourceRepository = unitOfWork.Repository<Resource>();
        _userRepository = unitOfWork.Repository<User>();
        _storageUsageService = storageUsageService;
    }

    public async Task<Result<AdminStorageUsageResponse>> Handle(
        GetAdminStorageUsageQuery request,
        CancellationToken cancellationToken)
    {
        var usersQuery = _userRepository.GetAll()
            .AsNoTracking()
            .Where(user => !user.IsDeleted);

        if (request.UserId.HasValue)
        {
            usersQuery = usersQuery.Where(user => user.Id == request.UserId.Value);
        }

        var users = await usersQuery
            .OrderBy(user => user.Email)
            .ToListAsync(cancellationToken);

        var responseUsers = new List<AdminStorageUserUsageResponse>(users.Count);
        foreach (var user in users)
        {
            var usageResult = await _storageUsageService.GetUserUsageAsync(user.Id, cancellationToken);
            if (usageResult.IsFailure)
            {
                continue;
            }

            var usage = usageResult.Value;
            if (request.SubscriptionId.HasValue && usage.SubscriptionId != request.SubscriptionId.Value)
            {
                continue;
            }

            if (request.OverQuotaOnly && !usage.IsOverQuota)
            {
                continue;
            }

            var resourceCount = await _resourceRepository.GetAll()
                .AsNoTracking()
                .CountAsync(resource => resource.UserId == user.Id && !resource.IsDeleted, cancellationToken);

            responseUsers.Add(new AdminStorageUserUsageResponse(
                user.Id,
                user.Email,
                usage.SubscriptionId,
                usage.SubscriptionName,
                usage.QuotaBytes,
                usage.UsedBytes,
                usage.AvailableBytes,
                usage.UsagePercent,
                usage.IsOverQuota,
                resourceCount));
        }

        return Result.Success(new AdminStorageUsageResponse(
            responseUsers.Sum(item => item.UsedBytes),
            responseUsers.Sum(item => item.ResourceCount),
            responseUsers));
    }
}
