using Application.Abstractions;
using Domain.Repositories;
using MassTransit;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.VideoGenerating;
using SharedLibrary.Extensions;

namespace Application.Veo.Commands;

public sealed record ExtendVideoCommand(
    Guid UserId,
    Guid OriginalCorrelationId,
    string Prompt,
    int? Seeds = null,
    string? Watermark = null) : IRequest<Result<ExtendVideoCommandResponse>>;

public sealed record ExtendVideoCommandResponse(Guid CorrelationId);

public sealed class ExtendVideoCommandHandler
    : IRequestHandler<ExtendVideoCommand, Result<ExtendVideoCommandResponse>>
{
    private readonly IVideoTaskRepository _videoTaskRepository;
    private readonly IBus _bus;

    public ExtendVideoCommandHandler(IVideoTaskRepository videoTaskRepository, IBus bus)
    {
        _videoTaskRepository = videoTaskRepository;
        _bus = bus;
    }

    public async Task<Result<ExtendVideoCommandResponse>> Handle(
        ExtendVideoCommand request,
        CancellationToken cancellationToken)
    {
        if (request.OriginalCorrelationId == Guid.Empty)
        {
            return Result.Failure<ExtendVideoCommandResponse>(VeoErrors.InvalidCorrelationId);
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return Result.Failure<ExtendVideoCommandResponse>(VeoErrors.InvalidPrompt);
        }

        var originalTask = await _videoTaskRepository.GetByCorrelationIdAsync(request.OriginalCorrelationId, cancellationToken);

        if (originalTask is null)
        {
            return Result.Failure<ExtendVideoCommandResponse>(VeoErrors.TaskNotFound);
        }

        if (string.IsNullOrEmpty(originalTask.VeoTaskId))
        {
            return Result.Failure<ExtendVideoCommandResponse>(VeoErrors.TaskNotCompleted);
        }

        var correlationId = Guid.CreateVersion7();

        var message = new VideoExtensionStarted
        {
            CorrelationId = correlationId,
            UserId = request.UserId,
            OriginalVeoTaskId = originalTask.VeoTaskId,
            Prompt = request.Prompt,
            Seeds = request.Seeds,
            Watermark = request.Watermark,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _bus.Publish(message, cancellationToken);

        return Result.Success(new ExtendVideoCommandResponse(correlationId));
    }
}
