using MassTransit;
using SharedLibrary.Contracts.VideoGenerating;

namespace Infrastructure.Sagas;

public class VideoTaskStateMachine : MassTransitStateMachine<VideoTaskState>
{
    public VideoTaskStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => VideoGenerationStarted, x => x.CorrelateById(m => m.Message.CorrelationId));
        Event(() => VideoTaskCreated, x => x.CorrelateById(m => m.Message.CorrelationId));
        Event(() => VideoGenerationCompleted, x => x.CorrelateById(m => m.Message.CorrelationId));
        Event(() => VideoGenerationFailed, x => x.CorrelateById(m => m.Message.CorrelationId));

        Schedule(() => GenerationTimeout, x => x.TimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(10);
            s.Received = r => r.CorrelateById(m => m.Message.CorrelationId);
        });

        Initially(
            When(VideoGenerationStarted)
                .Then(context =>
                {
                    context.Saga.VideoTaskId = Guid.CreateVersion7();
                    context.Saga.Prompt = context.Message.Prompt;
                    context.Saga.Model = context.Message.Model;
                    context.Saga.AspectRatio = context.Message.AspectRatio;
                    context.Saga.ImageUrls = context.Message.ImageUrls;
                    context.Saga.GenerationType = context.Message.GenerationType;
                    context.Saga.Seeds = context.Message.Seeds;
                    context.Saga.EnableTranslation = context.Message.EnableTranslation;
                    context.Saga.Watermark = context.Message.Watermark;
                    context.Saga.CreatedAt = context.Message.CreatedAt;
                })
                .Schedule(GenerationTimeout, context => context.Init<VideoGenerationTimeout>(new
                {
                    context.Saga.CorrelationId
                }))
                .TransitionTo(Submitted)
        );

        During(Submitted,
            When(VideoTaskCreated)
                .Then(context =>
                {
                    context.Saga.VeoTaskId = context.Message.VeoTaskId;
                })
                .TransitionTo(Processing)
        );

        During(Processing,
            When(VideoGenerationCompleted)
                .Then(context =>
                {
                    context.Saga.CompletedAt = context.Message.CompletedAt;
                })
                .Unschedule(GenerationTimeout)
                .TransitionTo(Completed)
                .Finalize(),

            When(VideoGenerationFailed)
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
            When(VideoGenerationFailed)
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

    public Event<VideoGenerationStarted> VideoGenerationStarted { get; private set; } = null!;
    public Event<VideoTaskCreated> VideoTaskCreated { get; private set; } = null!;
    public Event<VideoGenerationCompleted> VideoGenerationCompleted { get; private set; } = null!;
    public Event<VideoGenerationFailed> VideoGenerationFailed { get; private set; } = null!;

    public Schedule<VideoTaskState, VideoGenerationTimeout> GenerationTimeout { get; private set; } = null!;
}

public class VideoGenerationTimeout
{
    public Guid CorrelationId { get; set; }
}
