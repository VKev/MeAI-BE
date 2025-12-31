using Application.Configs.Models;
using Domain.Entities;
using SharedLibrary.Extensions;

namespace Application.Configs;

internal static class ConfigMapping
{
    internal static ConfigResponse ToResponse(Config config) =>
        new(
            config.Id,
            config.ChatModel,
            config.MediaAspectRatio,
            config.NumberOfVariances,
            config.CreatedAt ?? DateTimeExtensions.PostgreSqlUtcNow,
            config.UpdatedAt);
}
