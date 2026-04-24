using Application.Abstractions.Data;
using Application.Resources.Models;
using Application.Subscriptions.Services;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Resources.Queries;

public sealed record GetStorageSettingsQuery : IRequest<Result<StorageSettingsResponse>>;

public sealed class GetStorageSettingsQueryHandler
    : IRequestHandler<GetStorageSettingsQuery, Result<StorageSettingsResponse>>
{
    private readonly IRepository<Config> _repository;

    public GetStorageSettingsQueryHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Config>();
    }

    public async Task<Result<StorageSettingsResponse>> Handle(
        GetStorageSettingsQuery request,
        CancellationToken cancellationToken)
    {
        var config = await _repository.GetAll()
            .AsNoTracking()
            .Where(item => !item.IsDeleted)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var quotaBytes = config?.FreeStorageQuotaBytes
            ?? UserSubscriptionEntitlement.DefaultFreeStorageQuotaBytes;

        return Result.Success(new StorageSettingsResponse(
            quotaBytes,
            Math.Round(quotaBytes / 1024m / 1024m, 2),
            config?.UpdatedAt));
    }
}
