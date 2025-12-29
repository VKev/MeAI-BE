using System.Linq;
using Application.Configs.Contracts;
using Domain.Entities;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Configs.Queries.GetConfig;

internal sealed class GetConfigQueryHandler(IRepository<Config> configRepository)
    : IQueryHandler<GetConfigQuery, ConfigResponse>
{
    public async Task<Result<ConfigResponse>> Handle(GetConfigQuery request,
        CancellationToken cancellationToken)
    {
        var configs = await configRepository.FindAsync(config => !config.IsDeleted, cancellationToken);
        var config = configs.OrderByDescending(item => item.CreatedAt).FirstOrDefault();

        if (config == null)
        {
            return Result.Failure<ConfigResponse>(new Error("Config.NotFound", "Config not found"));
        }

        return Result.Success(ConfigMapping.ToResponse(config));
    }
}
