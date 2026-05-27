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

namespace Application.Recommendations.Commands;

public sealed record StartAnalysisSuggestionCommand(
    Guid UserId,
    Guid SocialMediaId,
    AnalysisSuggestionRequest Request) : IRequest<Result<AnalysisSuggestionStatusResponse>>;

public sealed class StartAnalysisSuggestionCommandHandler
    : IRequestHandler<StartAnalysisSuggestionCommand, Result<AnalysisSuggestionStatusResponse>>
{
#pragma warning disable CS0169
    private readonly RecommendPost? _domainDependency;
#pragma warning restore CS0169

    private readonly ISocialAccountAnalysisSuggestionRepository _repository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<StartAnalysisSuggestionCommandHandler> _logger;

    public StartAnalysisSuggestionCommandHandler(
        ISocialAccountAnalysisSuggestionRepository repository,
        IPublishEndpoint publishEndpoint,
        ILogger<StartAnalysisSuggestionCommandHandler> logger)
    {
        _repository = repository;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<Result<AnalysisSuggestionStatusResponse>> Handle(
        StartAnalysisSuggestionCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Request.From.HasValue &&
            request.Request.To.HasValue &&
            request.Request.From.Value > request.Request.To.Value)
        {
            return Result.Failure<AnalysisSuggestionStatusResponse>(
                new Error("AnalysisSuggest.InvalidPeriod", "from must be earlier than or equal to to."));
        }

        var now = DateTime.UtcNow;
        var correlationId = Guid.CreateVersion7();
        var entity = await _repository.GetByUserAndSocialMediaAsync(
            request.UserId,
            request.SocialMediaId,
            cancellationToken);
        var isNewEntity = false;

        if (entity is null)
        {
            isNewEntity = true;
            entity = new SocialAccountAnalysisSuggestion
            {
                Id = Guid.CreateVersion7(),
                CreatedAt = now,
            };
            await _repository.AddAsync(entity, cancellationToken);
        }

        entity.CorrelationId = correlationId;
        entity.UserId = request.UserId;
        entity.SocialMediaId = request.SocialMediaId;
        entity.Status = SocialAccountAnalysisSuggestionStatuses.Processing;
        entity.Platform = string.IsNullOrWhiteSpace(entity.Platform) ? "unknown" : entity.Platform;
        entity.Suggestion = null;
        entity.ErrorCode = null;
        entity.ErrorMessage = null;
        entity.RequestJson = JsonSerializer.Serialize(request.Request);
        entity.ResponseJson = null;
        entity.UpdatedAt = now;
        entity.CompletedAt = null;
        if (!isNewEntity)
        {
            _repository.Update(entity);
        }

        await _repository.SaveChangesAsync(cancellationToken);

        await PublishNotificationAsync(
            request.UserId,
            NotificationTypes.AiAccountAnalysisSuggestionProcessing,
            "Account analysis started",
            "AI is analyzing the account and recent post performance.",
            entity,
            null,
            cancellationToken);

        await _publishEndpoint.Publish(
            new GenerateAnalysisSuggestionStarted
            {
                CorrelationId = correlationId,
                UserId = request.UserId,
                SocialMediaId = request.SocialMediaId,
                From = request.Request.From,
                To = request.Request.To,
                PostLimit = request.Request.PostLimit,
                TopK = request.Request.TopK,
                MaxRagPosts = request.Request.MaxRagPosts,
                RefreshIndex = request.Request.RefreshIndex,
                Instruction = request.Request.Instruction,
                StartedAt = now,
            },
            cancellationToken);

        _logger.LogInformation(
            "Account analysis suggestion queued. CorrelationId={CorrelationId} UserId={UserId} SocialMediaId={SocialMediaId}",
            correlationId,
            request.UserId,
            request.SocialMediaId);

        return Result.Success(GetAnalysisSuggestionStatusQueryHandler.MapToResponse(entity));
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
