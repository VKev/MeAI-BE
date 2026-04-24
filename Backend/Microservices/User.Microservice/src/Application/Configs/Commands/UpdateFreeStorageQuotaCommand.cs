using Application.Abstractions.Data;
using Application.Resources.Models;
using Application.Subscriptions.Services;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Configs.Commands;

public sealed record UpdateFreeStorageQuotaCommand(long FreeStorageQuotaBytes) : IRequest<Result<StorageSettingsResponse>>;

public sealed class UpdateFreeStorageQuotaCommandHandler
    : IRequestHandler<UpdateFreeStorageQuotaCommand, Result<StorageSettingsResponse>>
{
    private readonly IRepository<Config> _repository;

    public UpdateFreeStorageQuotaCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Config>();
    }

    public async Task<Result<StorageSettingsResponse>> Handle(
        UpdateFreeStorageQuotaCommand request,
        CancellationToken cancellationToken)
    {
        if (request.FreeStorageQuotaBytes < 0)
        {
            return Result.Failure<StorageSettingsResponse>(
                new Error("Config.InvalidFreeStorageQuota", "Free storage quota must be greater than or equal to 0."));
        }

        var config = await _repository.GetAll()
            .Where(item => !item.IsDeleted)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (config is null)
        {
            config = new Config
            {
                Id = Guid.CreateVersion7(),
                MediaAspectRatio = "1:1",
                CreatedAt = DateTimeExtensions.PostgreSqlUtcNow,
                IsDeleted = false
            };

            await _repository.AddAsync(config, cancellationToken);
        }

        config.FreeStorageQuotaBytes = request.FreeStorageQuotaBytes;
        config.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _repository.Update(config);

        return Result.Success(new StorageSettingsResponse(
            config.FreeStorageQuotaBytes ?? UserSubscriptionEntitlement.DefaultFreeStorageQuotaBytes,
            Math.Round((config.FreeStorageQuotaBytes ?? UserSubscriptionEntitlement.DefaultFreeStorageQuotaBytes) / 1024m / 1024m, 2),
            config.UpdatedAt));
    }
}
