using MassTransit;
using SharedLibrary.Contracts.ImageGenerating;

namespace Infrastructure.Logic.Sagas;

public class ImageTaskStateMachine : MassTransitStateMachine<ImageTaskState>
{
    public ImageTaskStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => ImageGenerationStarted, x => x.CorrelateById(m => m.Message.CorrelationId));
        Event(() => ImageTaskCreated, x => x.CorrelateById(m => m.Message.CorrelationId));
        Event(() => ImageGenerationCompleted, x => x.CorrelateById(m => m.Message.CorrelationId));
        Event(() => ImageGenerationFailed, x => x.CorrelateById(m => m.Message.CorrelationId));

        Schedule(() => GenerationTimeout, x => x.TimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(5); // Image generation is typically faster
            s.Received = r => r.CorrelateById(m => m.Message.CorrelationId);
        });

        Initially(
            When(ImageGenerationStarted)
                .Then(context =>
                {
                    context.Saga.ImageTaskId = Guid.CreateVersion7();
                    context.Saga.Prompt = context.Message.Prompt;
                    context.Saga.AspectRatio = context.Message.AspectRatio;
                    context.Saga.Resolution = context.Message.Resolution;
                    context.Saga.OutputFormat = context.Message.OutputFormat;
                    context.Saga.ImageUrls = context.Message.ImageUrls;
                    context.Saga.CreatedAt = context.Message.CreatedAt;
                })
                .Schedule(GenerationTimeout, context => context.Init<ImageGenerationTimeout>(new
                {
                    context.Saga.CorrelationId
                }))
                .TransitionTo(Submitted)
        );

        During(Submitted,
            When(ImageTaskCreated)
                .Then(context =>
                {
                    context.Saga.KieTaskId = context.Message.KieTaskId;
                })
                .TransitionTo(Processing)
        );

        During(Processing,
            When(ImageGenerationCompleted)
                .Then(context =>
                {
                    context.Saga.CompletedAt = context.Message.CompletedAt;
                })
                .Unschedule(GenerationTimeout)
                .TransitionTo(Completed)
                .Finalize(),

            When(ImageGenerationFailed)
                .Then(context =>
                {
                    context.Saga.CompletedAt = context.Message.FailedAt;
                })
                .Unschedule(GenerationTimeout)
                .TransitionTo(Failed)
                .Finalize(),

            When(GenerationTimeout.Received)
                .Then(context =>
                {
                    context.Saga.CompletedAt = DateTime.UtcNow;
                })
                .TransitionTo(Failed)
                .Finalize()
        );

        During(Submitted,
            When(ImageGenerationFailed)
                .Then(context =>
                {
                    context.Saga.CompletedAt = context.Message.FailedAt;
                })
                .Unschedule(GenerationTimeout)
                .TransitionTo(Failed)
                .Finalize(),

            When(GenerationTimeout.Received)
                .Then(context =>
                {
                    context.Saga.CompletedAt = DateTime.UtcNow;
                })
                .TransitionTo(Failed)
                .Finalize()
        );

        SetCompletedWhenFinalized();
    }

    public State Submitted { get; private set; } = null!;
    public State Processing { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Failed { get; private set; } = null!;

    public Event<ImageGenerationStarted> ImageGenerationStarted { get; private set; } = null!;
    public Event<ImageTaskCreated> ImageTaskCreated { get; private set; } = null!;
    public Event<ImageGenerationCompleted> ImageGenerationCompleted { get; private set; } = null!;
    public Event<ImageGenerationFailed> ImageGenerationFailed { get; private set; } = null!;

    public Schedule<ImageTaskState, ImageGenerationTimeout> GenerationTimeout { get; private set; } = null!;
}

public class ImageGenerationTimeout
{
    public Guid CorrelationId { get; set; }
}
