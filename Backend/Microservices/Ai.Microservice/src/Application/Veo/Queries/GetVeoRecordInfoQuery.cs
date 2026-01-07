using Application.Abstractions;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Veo.Queries;

public sealed record GetVeoRecordInfoQuery(Guid CorrelationId) : IRequest<Result<VeoRecordInfoResult>>;

