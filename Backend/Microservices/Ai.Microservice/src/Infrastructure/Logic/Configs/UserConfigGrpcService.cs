using Application.Abstractions.Configs;
using Grpc.Core;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Grpc.UserResources;

namespace Infrastructure.Logic.Configs;

public sealed class UserConfigGrpcService : IUserConfigService
{
    private readonly UserResourceService.UserResourceServiceClient _client;

    public UserConfigGrpcService(UserResourceService.UserResourceServiceClient client)
    {
        _client = client;
    }

    public async Task<Result<UserAiConfig?>> GetActiveConfigAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetActiveConfigAsync(
                new GetActiveConfigRequest(),
                cancellationToken: cancellationToken);

            if (!response.HasActiveConfig || !Guid.TryParse(response.ConfigId, out var configId))
            {
                return Result.Success<UserAiConfig?>(null);
            }

            int? numberOfVariances = response.NumberOfVariances > 0
                ? response.NumberOfVariances
                : null;

            return Result.Success<UserAiConfig?>(new UserAiConfig(
                configId,
                string.IsNullOrWhiteSpace(response.ChatModel) ? null : response.ChatModel,
                string.IsNullOrWhiteSpace(response.MediaAspectRatio) ? null : response.MediaAspectRatio,
                numberOfVariances));
        }
        catch (RpcException ex)
        {
            return Result.Failure<UserAiConfig?>(
                new Error("UserConfig.GrpcError", ex.Status.Detail));
        }
    }
}
