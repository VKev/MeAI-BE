using Application.Configs.Contracts;
using SharedLibrary.Abstractions.Messaging;

namespace Application.Configs.Commands.UpdateConfig;

public sealed record UpdateConfigCommand(
    string? ChatModel,
    string? MediaAspectRatio,
    int? NumberOfVariances) : ICommand<ConfigResponse>;
