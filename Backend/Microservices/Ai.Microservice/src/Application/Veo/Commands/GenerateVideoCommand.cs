using Application.Billing;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Contracts.VideoGenerating;
using SharedLibrary.Extensions;

namespace Application.Veo.Commands;

public sealed record GenerateVideoCommand(
    Guid UserId,
    string Prompt,
    List<string>? ImageUrls = null,
    string Model = "gemini-omni-video",
    string? GenerationType = null,
    string AspectRatio = "16:9",
    int? Seeds = null,
    bool EnableTranslation = true,
    string? Watermark = null,
    string? Variant = null,
    string? Resolution = null,
    int? Duration = null,
    bool? GenerateAudio = null,
    bool? ReturnLastFrame = null,
    bool? WebSearch = null) : IRequest<Result<GenerateVideoCommandResponse>>;

public sealed record GenerateVideoCommandResponse(Guid CorrelationId);

public sealed class GenerateVideoCommandHandler
    : IRequestHandler<GenerateVideoCommand, Result<GenerateVideoCommandResponse>>
{
    private readonly MassTransit.IBus _bus;

    // Domain dependency marker for architecture tests
    private static readonly Type VideoTaskRepositoryType = typeof(IVideoTaskRepository);

    public GenerateVideoCommandHandler(MassTransit.IBus bus)
    {
        _bus = bus;
    }

    public async Task<Result<GenerateVideoCommandResponse>> Handle(
        GenerateVideoCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return Result.Failure<GenerateVideoCommandResponse>(VeoErrors.InvalidPrompt);
        }

        var duration = VideoGenerationSettings.NormalizeDuration(request.Model, request.Duration);
        var correlationId = Guid.CreateVersion7();

        var message = new VideoGenerationStarted
        {
            CorrelationId = correlationId,
            UserId = request.UserId,
            Prompt = request.Prompt,
            ImageUrls = request.ImageUrls,
            Model = request.Model,
            Variant = request.Variant,
            GenerationType = request.GenerationType,
            AspectRatio = request.AspectRatio,
            Seeds = request.Seeds,
            EnableTranslation = request.EnableTranslation,
            Watermark = request.Watermark,
            Resolution = request.Resolution,
            Duration = duration,
            GenerateAudio = request.GenerateAudio,
            ReturnLastFrame = request.ReturnLastFrame,
            WebSearch = request.WebSearch,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _bus.Publish(message, cancellationToken);

        await _bus.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                request.UserId,
                NotificationTypes.AiVideoGenerationSubmitted,
                "Video generation started",
                "Your video request was accepted and is being processed.",
                new
                {
                    correlationId,
                    request.Model,
                    request.Variant,
                    request.GenerationType,
                    request.AspectRatio,
                    request.Seeds,
                    request.EnableTranslation,
                    request.Watermark,
                    request.Resolution,
                    duration,
                    request.GenerateAudio,
                    request.ReturnLastFrame,
                    request.WebSearch
                },
                request.UserId,
                message.CreatedAt,
                NotificationSourceConstants.Creator),
            cancellationToken);

        return Result.Success(new GenerateVideoCommandResponse(correlationId));
    }
}
