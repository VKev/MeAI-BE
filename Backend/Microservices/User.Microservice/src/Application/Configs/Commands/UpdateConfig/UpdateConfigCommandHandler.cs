using System.Linq;
using Application.Configs.Contracts;
using Domain.Entities;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Configs.Commands.UpdateConfig;

internal sealed class UpdateConfigCommandHandler(IRepository<Config> configRepository)
    : ICommandHandler<UpdateConfigCommand, ConfigResponse>
{
    public async Task<Result<ConfigResponse>> Handle(UpdateConfigCommand request,
        CancellationToken cancellationToken)
    {
        var configs = await configRepository.FindAsync(config => !config.IsDeleted, cancellationToken);
        var config = configs.OrderByDescending(item => item.CreatedAt).FirstOrDefault();
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
            await configRepository.AddAsync(config, cancellationToken);
        }
        else
        {
            configRepository.Update(config);
        }

        return Result.Success(ConfigMapping.ToResponse(config));
    }
}
