using Application.Configs.Contracts;
using SharedLibrary.Abstractions.Messaging;

namespace Application.Configs.Queries.GetConfig;

public sealed record GetConfigQuery : IQuery<ConfigResponse>;
