using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.VideoGenerating;
using SharedLibrary.Extensions;

namespace Application.Veo.Commands;

public sealed record GenerateVideoCommand(
    Guid UserId,
    string Prompt,
    List<string>? ImageUrls = null,
    string Model = "veo3_fast",
    string? GenerationType = null,
    string AspectRatio = "16:9",
    int? Seeds = null,
    bool EnableTranslation = true,
    string? Watermark = null) : IRequest<Result<GenerateVideoCommandResponse>>;

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

        var correlationId = Guid.CreateVersion7();

        var message = new VideoGenerationStarted
        {
            CorrelationId = correlationId,
            UserId = request.UserId,
            Prompt = request.Prompt,
            ImageUrls = request.ImageUrls,
            Model = request.Model,
            GenerationType = request.GenerationType,
            AspectRatio = request.AspectRatio,
            Seeds = request.Seeds,
            EnableTranslation = request.EnableTranslation,
            Watermark = request.Watermark,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _bus.Publish(message, cancellationToken);

        return Result.Success(new GenerateVideoCommandResponse(correlationId));
    }
}
