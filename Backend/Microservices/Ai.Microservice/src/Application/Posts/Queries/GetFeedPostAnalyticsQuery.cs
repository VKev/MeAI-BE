using Application.Abstractions.Feed;
using Application.Posts.Models;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Queries;

public sealed record GetFeedPostAnalyticsQuery(
    Guid UserId,
    Guid PostId,
    int? CommentSampleLimit) : IRequest<Result<SocialPlatformPostAnalyticsResponse>>;

public sealed class GetFeedPostAnalyticsQueryHandler
    : IRequestHandler<GetFeedPostAnalyticsQuery, Result<SocialPlatformPostAnalyticsResponse>>
{
    private static readonly Post? DomainDependency = null;

    private readonly IFeedAnalyticsService _feedAnalyticsService;

    public GetFeedPostAnalyticsQueryHandler(IFeedAnalyticsService feedAnalyticsService)
    {
        _feedAnalyticsService = feedAnalyticsService;
    }

    public Task<Result<SocialPlatformPostAnalyticsResponse>> Handle(
        GetFeedPostAnalyticsQuery request,
        CancellationToken cancellationToken)
    {
        _ = DomainDependency;

        return _feedAnalyticsService.GetPostAnalyticsAsync(
            request.UserId,
            request.PostId,
            request.CommentSampleLimit,
            cancellationToken);
    }
}
