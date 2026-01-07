using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Veo.Commands;

public sealed record ExtendVideoCommand(
    Guid OriginalCorrelationId,
    string Prompt,
    int? Seeds = null,
    string? Watermark = null) : IRequest<Result<ExtendVideoCommandResponse>>;

public sealed record ExtendVideoCommandResponse(Guid CorrelationId);

