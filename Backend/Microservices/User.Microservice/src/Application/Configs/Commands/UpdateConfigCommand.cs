using Application.Abstractions.Data;
using Application.Configs.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Configs.Commands;

public sealed record UpdateConfigCommand(
    string? ChatModel,
    string? MediaAspectRatio,
    int? NumberOfVariances) : IRequest<Result<ConfigResponse>>;

public sealed class UpdateConfigCommandHandler
    : IRequestHandler<UpdateConfigCommand, Result<ConfigResponse>>
{
    private readonly IRepository<Config> _repository;

    public UpdateConfigCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Config>();
    }

    public async Task<Result<ConfigResponse>> Handle(UpdateConfigCommand request,
        CancellationToken cancellationToken)
    {
        var config = await _repository.GetAll()
            .Where(item => !item.IsDeleted)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var isNew = false;
        if (config == null)
        {
            config = new Config
            {
                Id = Guid.CreateVersion7(),
                CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
            };
            isNew = true;
        }

        config.ChatModel = request.ChatModel?.Trim();
        config.MediaAspectRatio = request.MediaAspectRatio?.Trim();
        config.NumberOfVariances = request.NumberOfVariances;
        config.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        if (isNew)
        {
            await _repository.AddAsync(config, cancellationToken);
        }
        else
        {
            _repository.Update(config);
        }

        return Result.Success(ConfigMapping.ToResponse(config));
    }
}
