using Application.Configs.Contracts;
using Domain.Entities;

namespace Application.Configs;

internal static class ConfigMapping
{
    internal static ConfigResponse ToResponse(Config config) =>
        new(
            config.Id,
            config.ChatModel,
            config.MediaAspectRatio,
            config.NumberOfVariances,
            config.CreatedAt,
            config.UpdatedAt);
}
