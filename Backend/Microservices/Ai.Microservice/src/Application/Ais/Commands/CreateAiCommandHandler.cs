using System;
using System.Collections.Generic;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Abstractions.Messaging;
using Application.Common;
using Domain.Entities;
using Domain.Repositories;
using MassTransit;
using SharedLibrary.Contracts.UserCreating;

namespace Application.Ais.Commands
{
    public sealed record CreateAiCommand(
        string Fullname,
        string Email,
        string? PhoneNumber = null,
        DateTime? DateOfBirth = null,
        string? Gender = null
    ) : ICommand;
    internal sealed class CreateAiCommandHandler : ICommandHandler<CreateAiCommand>
    {
        private readonly IAiRepository _aiRepository;
        private readonly IPublishEndpoint _publishEndpoint;

        public CreateAiCommandHandler(IAiRepository aiRepository, IPublishEndpoint publishEndpoint)
        {
            _aiRepository = aiRepository;
            _publishEndpoint = publishEndpoint;
        }
        public async Task<Result> Handle(CreateAiCommand command, CancellationToken cancellationToken)
        {
            var existing = await _aiRepository.GetByEmailAsync(command.Email, cancellationToken);
            if (existing != null)
            {
                return Result.Failure(new Error("Ai.EmailExists", "Ai already exists with this email."));
            }

            var correlationId = Guid.NewGuid();
            var generatedPassword = $"Ai-{Guid.NewGuid():N}";

            var ai = Ai.Create(command.Fullname, command.Email, command.PhoneNumber);
            await _aiRepository.AddAsync(ai, cancellationToken);

            await _publishEndpoint.Publish(new AiCreatedEvent
            {
                CorrelationId = correlationId,
                Name = command.Fullname,
                Email = command.Email,
                Password = generatedPassword,
                ProviderName = "ai-service",
                ProviderUserId = command.Email,
                DateOfBirth = command.DateOfBirth,
                Gender = string.IsNullOrWhiteSpace(command.Gender) ? "Unknown" : command.Gender,
                PhoneNumber = command.PhoneNumber ?? string.Empty
            }, cancellationToken);

            return Result.Success();
        }
    }
}
