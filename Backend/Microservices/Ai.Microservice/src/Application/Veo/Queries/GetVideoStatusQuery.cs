using Application.Veo.Models;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Veo.Queries;

public sealed record GetVideoStatusQuery(Guid CorrelationId) : IRequest<Result<VideoTaskStatusResponse>>;

public sealed record VideoTaskStatusResponse(
    Guid Id,
    Guid CorrelationId,
    string? VeoTaskId,
    string Status,
    string? ResultUrls,
    string? Resolution,
    int? ErrorCode,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? CompletedAt);
