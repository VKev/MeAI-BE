using Application.Recommendations.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Recommendations.Queries;

public sealed record GetAnalysisSuggestionStatusQuery(
    Guid UserId,
    Guid SocialMediaId) : IRequest<Result<AnalysisSuggestionStatusResponse>>;

public sealed class GetAnalysisSuggestionStatusQueryHandler
    : IRequestHandler<GetAnalysisSuggestionStatusQuery, Result<AnalysisSuggestionStatusResponse>>
{
#pragma warning disable CS0169
    private readonly RecommendPost? _domainDependency;
#pragma warning restore CS0169

    private readonly ISocialAccountAnalysisSuggestionRepository _repository;

    public GetAnalysisSuggestionStatusQueryHandler(ISocialAccountAnalysisSuggestionRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<AnalysisSuggestionStatusResponse>> Handle(
        GetAnalysisSuggestionStatusQuery request,
        CancellationToken cancellationToken)
    {
        var suggestion = await _repository.GetByUserAndSocialMediaAsync(
            request.UserId,
            request.SocialMediaId,
            cancellationToken);

        if (suggestion is null)
        {
            return Result.Success(new AnalysisSuggestionStatusResponse(
                SocialMediaId: request.SocialMediaId,
                Platform: "unknown",
                Status: "NotSuggested",
                IsSuggested: false,
                CorrelationId: null,
                Suggestion: null,
                GeneratedAt: null,
                CompletedAt: null,
                ErrorCode: null,
                ErrorMessage: null));
        }

        return Result.Success(MapToResponse(suggestion));
    }

    internal static AnalysisSuggestionStatusResponse MapToResponse(SocialAccountAnalysisSuggestion suggestion)
    {
        var isSuggested =
            string.Equals(suggestion.Status, SocialAccountAnalysisSuggestionStatuses.Completed, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(suggestion.Suggestion);

        return new AnalysisSuggestionStatusResponse(
            SocialMediaId: suggestion.SocialMediaId,
            Platform: suggestion.Platform,
            Status: suggestion.Status,
            IsSuggested: isSuggested,
            CorrelationId: suggestion.CorrelationId,
            Suggestion: suggestion.Suggestion,
            GeneratedAt: suggestion.UpdatedAt,
            CompletedAt: suggestion.CompletedAt,
            ErrorCode: suggestion.ErrorCode,
            ErrorMessage: suggestion.ErrorMessage);
    }
}
