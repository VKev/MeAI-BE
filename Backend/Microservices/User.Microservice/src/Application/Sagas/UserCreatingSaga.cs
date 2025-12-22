using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MassTransit;
using SharedLibrary.Contracts.UserCreating;

namespace Application.Sagas
{
    public class UserCreatingSaga : MassTransitStateMachine<UserCreatingSagaData>
    {
        public State AiCreating { get; set; } = null!;
        public State Completed { get; set; } = null!;
        public State Failed { get; set; } = null!;


        public Event<UserCreatingSagaStart> userCreated { get; set; } = null!;
        public Event<AiCreatedEvent> AiCreated { get; set; } = null!;
        public Event<AiCreatedFailureEvent> AiCreatedFailed { get; set; } = null!;

        public UserCreatingSaga()
        {
            InstanceState(x => x.CurrentState);

            Event(() => userCreated, e => e.CorrelateById(m => m.Message.CorrelationId));
            Event(() => AiCreated, e => e.CorrelateById(m => m.Message.CorrelationId));
            Event(() => AiCreatedFailed, e => e.CorrelateById(m => m.Message.CorrelationId));

            Initially(
                When(userCreated)
                .TransitionTo(AiCreating)
                .ThenAsync(async context =>
                {
                    context.Saga.CorrelationId = context.Message.CorrelationId;
                    context.Saga.UserCreated = true;

                    await context.Publish(new UserCreatedEvent
                    {
                        CorrelationId = context.Message.CorrelationId,
                        Name = context.Message.Name,
                        Email = context.Message.Email
                    });
                })
            );

            During(AiCreating,
                When(AiCreated)
                    .Then(context =>
                    {
                        context.Saga.AiCreated = true;
                    })
                    .TransitionTo(Completed),

                When(AiCreatedFailed)
                    .Then(context =>
                    {
                        Console.WriteLine($"Ai creation failed: {context.Message.Reason}");
                    })
                    .TransitionTo(Failed)
            );

            During(Completed,
                When(AiCreated)
                    .Then(context =>
                    {
                        // Ignore duplicate AiCreated events after completion (e.g., redeliveries)
                    }),
                When(AiCreatedFailed)
                    .Then(context =>
                    {
                        // Ignore late failure messages once the saga is already completed
                    }));

            SetCompletedWhenFinalized();
        }
    }
}
