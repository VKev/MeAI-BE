using Application.Abstractions;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Veo.Queries;

public sealed record Get1080PVideoQuery(Guid CorrelationId, int Index = 0) : IRequest<Result<Veo1080PResult>>;

