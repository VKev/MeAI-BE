using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Application.Ais.Commands;
using MassTransit;
using SharedLibrary.Contracts.UserCreating;
using MediatR;

namespace Application.Consumers
{
    public class UserCreatedConsumer : IConsumer<UserCreatedEvent>
    {
        private readonly ISender _sender;

        public UserCreatedConsumer(ISender sender)
        {
            _sender = sender;
        }
        public async Task Consume(ConsumeContext<UserCreatedEvent> context)
        {
            var command = new CreateAiCommand(context.Message.Name, context.Message.Email);
            var result = await _sender.Send(command, context.CancellationToken);

            if (result.IsFailure)
            {
                // If the ai already exists, treat it as success to keep the saga flowing
                if (string.Equals(result.Error.Code, "Ai.EmailExists", StringComparison.OrdinalIgnoreCase))
                {
                    await context.Publish(new AiCreatedEvent
                    {
                        CorrelationId = context.Message.CorrelationId,
                        Name = context.Message.Name,
                        Email = context.Message.Email,
                        Password = string.Empty,
                        ProviderName = "user-service",
                        ProviderUserId = context.Message.Email
                    }, context.CancellationToken);
                    return;
                }

                await context.Publish(new AiCreatedFailureEvent
                {
                    CorrelationId = context.Message.CorrelationId,
                    Reason = result.Error.Description
                }, context.CancellationToken);
                return;
            }
            
            await context.Publish(new AiCreatedEvent
            {
                CorrelationId = context.Message.CorrelationId,
                Name = context.Message.Name,
                Email = context.Message.Email,
                Password = string.Empty,
                ProviderName = "user-service",
                ProviderUserId = context.Message.Email
            }, context.CancellationToken);
        }
    }
}
