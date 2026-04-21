using Application.Abstractions.Feed;
using Application.Posts.Models;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Queries;

public sealed record GetFeedDashboardSummaryQuery(
    Guid UserId,
    string Username,
    int? PostLimit) : IRequest<Result<SocialPlatformDashboardSummaryResponse>>;

public sealed class GetFeedDashboardSummaryQueryHandler
    : IRequestHandler<GetFeedDashboardSummaryQuery, Result<SocialPlatformDashboardSummaryResponse>>
{
    private static readonly Post? DomainDependency = null;

    private readonly IFeedAnalyticsService _feedAnalyticsService;

    public GetFeedDashboardSummaryQueryHandler(IFeedAnalyticsService feedAnalyticsService)
    {
        _feedAnalyticsService = feedAnalyticsService;
    }

    public Task<Result<SocialPlatformDashboardSummaryResponse>> Handle(
        GetFeedDashboardSummaryQuery request,
        CancellationToken cancellationToken)
    {
        _ = DomainDependency;

        return _feedAnalyticsService.GetDashboardSummaryAsync(
            request.UserId,
            request.Username,
            request.PostLimit,
            cancellationToken);
    }
}
