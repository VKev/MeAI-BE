using Application.Recommendations.Models;
using Application.Recommendations.Queries;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Notifications;
using SharedLibrary.Contracts.Recommendations;

namespace Infrastructure.Logic.Consumers;

public sealed class ContentSuggestionConsumer : IConsumer<GenerateContentSuggestionStarted>
{
    private readonly IMediator _mediator;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<ContentSuggestionConsumer> _logger;

    public ContentSuggestionConsumer(
        IMediator mediator,
        IPublishEndpoint publishEndpoint,
        ILogger<ContentSuggestionConsumer> logger)
    {
        _mediator = mediator;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<GenerateContentSuggestionStarted> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;
        var request = new ContentSuggestionRequest(
            Instruction: message.Instruction,
            Style: message.Style,
            MediaType: message.MediaType,
            WorkspaceId: message.WorkspaceId,
            TopK: message.TopK,
            MaxRagPosts: message.MaxRagPosts,
            RefreshIndex: message.RefreshIndex);

        Result<ContentSuggestionResponse> result;
        try
        {
            result = await _mediator.Send(
                new GenerateContentSuggestionQuery(
                    message.CorrelationId,
                    message.UserId,
                    message.SocialMediaId,
                    request),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Content suggestion threw. CorrelationId={CorrelationId} UserId={UserId} SocialMediaId={SocialMediaId}",
                message.CorrelationId,
                message.UserId,
                message.SocialMediaId);

            result = Result.Failure<ContentSuggestionResponse>(
                new Error("ContentSuggestion.Failed", $"Content suggestion failed: {ex.Message}"));
        }

        if (result.IsFailure)
        {
            await PublishFailedAsync(message, result.Error, cancellationToken);
            return;
        }

        await PublishCompletedAsync(message, result.Value, cancellationToken);
    }

    private Task PublishCompletedAsync(
        GenerateContentSuggestionStarted message,
        ContentSuggestionResponse response,
        CancellationToken cancellationToken)
    {
        return _publishEndpoint.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                message.UserId,
                NotificationTypes.AiContentSuggestionCompleted,
                "Content idea ready",
                "AI suggested a fresh prompt for your next recommendation draft.",
                new
                {
                    correlationId = message.CorrelationId,
                    socialMediaId = message.SocialMediaId,
                    workspaceId = message.WorkspaceId,
                    platform = response.Platform,
                    status = "Completed",
                    style = response.Style,
                    mediaType = response.MediaType,
                    instruction = message.Instruction,
                    userPrompt = response.UserPrompt,
                    recommendationSummary = response.RecommendationSummary,
                    webSources = response.WebSources,
                    references = response.References,
                    retrievalErrors = response.RetrievalErrors,
                    generatedAt = response.GeneratedAt,
                    completedAt = DateTime.UtcNow,
                    response,
                },
                source: NotificationSourceConstants.Creator),
            cancellationToken);
    }

    private Task PublishFailedAsync(
        GenerateContentSuggestionStarted message,
        Error error,
        CancellationToken cancellationToken)
    {
        return _publishEndpoint.Publish(
            NotificationRequestedEventFactory.CreateForUser(
                message.UserId,
                NotificationTypes.AiContentSuggestionFailed,
                "Content suggestion failed",
                error.Description,
                new
                {
                    correlationId = message.CorrelationId,
                    socialMediaId = message.SocialMediaId,
                    workspaceId = message.WorkspaceId,
                    status = "Failed",
                    style = message.Style,
                    mediaType = message.MediaType,
                    instruction = message.Instruction,
                    errorCode = error.Code,
                    errorMessage = error.Description,
                    completedAt = DateTime.UtcNow,
                },
                source: NotificationSourceConstants.Creator),
            cancellationToken);
    }
}
