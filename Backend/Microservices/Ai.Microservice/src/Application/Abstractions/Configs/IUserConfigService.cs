using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Configs;

public interface IUserConfigService
{
    Task<Result<UserAiConfig?>> GetActiveConfigAsync(CancellationToken cancellationToken);
}

public sealed record UserAiConfig(
    Guid Id,
    string? ChatModel,
    string? MediaAspectRatio,
    int? NumberOfVariances);
