using Application.Abstractions.Data;
using Application.Resources.Models;
using Application.Resources.Queries;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Resources.Commands;

public sealed record UpdateSystemStorageQuotaCommand(long? SystemStorageQuotaBytes)
    : IRequest<Result<SystemStorageSettingsResponse>>;

public sealed class UpdateSystemStorageQuotaCommandHandler
    : IRequestHandler<UpdateSystemStorageQuotaCommand, Result<SystemStorageSettingsResponse>>
{
    private readonly IRepository<Config> _configRepository;
    private readonly IRepository<Resource> _resourceRepository;

    public UpdateSystemStorageQuotaCommandHandler(IUnitOfWork unitOfWork)
    {
        _configRepository = unitOfWork.Repository<Config>();
        _resourceRepository = unitOfWork.Repository<Resource>();
    }

    public async Task<Result<SystemStorageSettingsResponse>> Handle(
        UpdateSystemStorageQuotaCommand request,
        CancellationToken cancellationToken)
    {
        if (request.SystemStorageQuotaBytes.HasValue && request.SystemStorageQuotaBytes.Value < 0)
        {
            return Result.Failure<SystemStorageSettingsResponse>(
                new Error("Config.InvalidSystemStorageQuota", "System storage quota must be greater than or equal to 0."));
        }

        var config = await _configRepository.GetAll()
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

            await _configRepository.AddAsync(config, cancellationToken);
        }

        config.SystemStorageQuotaBytes = request.SystemStorageQuotaBytes;
        config.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _configRepository.Update(config);

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

        return Result.Success(GetSystemStorageSettingsQueryHandler.BuildResponse(
            config.SystemStorageQuotaBytes,
            usedBytes,
            resourceCount,
            userCount,
            config.UpdatedAt));
    }
}
