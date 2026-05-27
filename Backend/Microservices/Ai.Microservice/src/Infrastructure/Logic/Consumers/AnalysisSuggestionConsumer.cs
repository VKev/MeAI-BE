using System.Text.Json;
using Application.Recommendations.Models;
using Application.Recommendations.Queries;
using Domain.Entities;
using Domain.Repositories;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Contracts.Recommendations;

namespace Infrastructure.Logic.Consumers;

public sealed class AnalysisSuggestionConsumer : IConsumer<GenerateAnalysisSuggestionStarted>
{
    private readonly ISocialAccountAnalysisSuggestionRepository _repository;
    private readonly IMediator _mediator;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<AnalysisSuggestionConsumer> _logger;

    public AnalysisSuggestionConsumer(
        ISocialAccountAnalysisSuggestionRepository repository,
        IMediator mediator,
        IPublishEndpoint publishEndpoint,
        ILogger<AnalysisSuggestionConsumer> logger)
    {
        _repository = repository;
        _mediator = mediator;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<GenerateAnalysisSuggestionStarted> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;

        var entity = await _repository.GetByUserAndSocialMediaAsync(
            message.UserId,
            message.SocialMediaId,
            cancellationToken);

        if (entity is null)
        {
            _logger.LogWarning(
                "Account analysis suggestion row missing. CorrelationId={CorrelationId} UserId={UserId} SocialMediaId={SocialMediaId}",
                message.CorrelationId,
                message.UserId,
                message.SocialMediaId);
            return;
        }

        if (entity.CorrelationId != message.CorrelationId)
        {
            _logger.LogInformation(
                "Skipping stale account analysis suggestion job. MessageCorrelationId={MessageCorrelationId} CurrentCorrelationId={CurrentCorrelationId} UserId={UserId} SocialMediaId={SocialMediaId}",
                message.CorrelationId,
                entity.CorrelationId,
                message.UserId,
                message.SocialMediaId);
            return;
        }

        var request = new AnalysisSuggestionRequest(
            message.From,
            message.To,
            message.PostLimit,
            message.TopK,
            message.MaxRagPosts,
            message.RefreshIndex,
            message.Instruction);

        Result<AnalysisSuggestionResponse> result;
        try
        {
            result = await _mediator.Send(
                new GenerateAnalysisSuggestionQuery(message.UserId, message.SocialMediaId, request),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Account analysis suggestion threw. CorrelationId={CorrelationId} UserId={UserId} SocialMediaId={SocialMediaId}",
                message.CorrelationId,
                message.UserId,
                message.SocialMediaId);

            result = Result.Failure<AnalysisSuggestionResponse>(
                new Error("AnalysisSuggest.Failed", $"Suggestion generation failed: {ex.Message}"));
        }

        if (result.IsFailure)
        {
            await MarkFailedAsync(entity, result.Error, cancellationToken);
            return;
        }

        await MarkCompletedAsync(entity, result.Value, cancellationToken);
    }

    private async Task MarkFailedAsync(
        SocialAccountAnalysisSuggestion entity,
        Error error,
        CancellationToken cancellationToken)
    {
        var failedAt = DateTime.UtcNow;
        entity.Status = SocialAccountAnalysisSuggestionStatuses.Failed;
        entity.ErrorCode = error.Code;
        entity.ErrorMessage = error.Description;
        entity.UpdatedAt = failedAt;
        entity.CompletedAt = failedAt;
        _repository.Update(entity);
        await _repository.SaveChangesAsync(cancellationToken);

        await PublishNotificationAsync(
            entity.UserId,
            NotificationTypes.AiAccountAnalysisSuggestionFailed,
            "Account analysis failed",
            error.Description,
            entity,
            null,
            cancellationToken);
    }

    private async Task MarkCompletedAsync(
        SocialAccountAnalysisSuggestion entity,
        AnalysisSuggestionResponse response,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTime.UtcNow;
        entity.Platform = response.Platform;
        entity.Status = SocialAccountAnalysisSuggestionStatuses.Completed;
        entity.Suggestion = response.Suggestion;
        entity.ResponseJson = JsonSerializer.Serialize(response);
        entity.UpdatedAt = completedAt;
        entity.CompletedAt = completedAt;
        _repository.Update(entity);
        await _repository.SaveChangesAsync(cancellationToken);

        await PublishNotificationAsync(
            entity.UserId,
            NotificationTypes.AiAccountAnalysisSuggestionCompleted,
            "Account analysis ready",
            "AI finished analyzing this social account.",
            entity,
            response,
            cancellationToken);

        _logger.LogInformation(
            "Account analysis suggestion completed. CorrelationId={CorrelationId} UserId={UserId} SocialMediaId={SocialMediaId}",
            entity.CorrelationId,
            entity.UserId,
            entity.SocialMediaId);
    }

    private Task PublishNotificationAsync(
        Guid userId,
        string type,
        string title,
        string message,
        SocialAccountAnalysisSuggestion entity,
        AnalysisSuggestionResponse? response,
        CancellationToken cancellationToken)
    {
        return _publishEndpoint.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                userId,
                type,
                title,
                message,
                new
                {
                    correlationId = entity.CorrelationId,
                    socialMediaId = entity.SocialMediaId,
                    platform = entity.Platform,
                    status = entity.Status,
                    isSuggested = string.Equals(
                        entity.Status,
                        SocialAccountAnalysisSuggestionStatuses.Completed,
                        StringComparison.OrdinalIgnoreCase),
                    suggestion = entity.Suggestion,
                    generatedAt = entity.UpdatedAt,
                    completedAt = entity.CompletedAt,
                    errorCode = entity.ErrorCode,
                    errorMessage = entity.ErrorMessage,
                    response,
                },
                source: NotificationSourceConstants.Creator),
            cancellationToken);
    }
}
