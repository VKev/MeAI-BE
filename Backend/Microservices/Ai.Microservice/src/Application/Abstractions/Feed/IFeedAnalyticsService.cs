using Application.Posts.Models;
using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Feed;

public interface IFeedAnalyticsService
{
    Task<Result<SocialPlatformDashboardSummaryResponse>> GetDashboardSummaryAsync(
        Guid requesterUserId,
        string username,
        int? postLimit,
        CancellationToken cancellationToken);

    Task<Result<SocialPlatformPostAnalyticsResponse>> GetPostAnalyticsAsync(
        Guid requesterUserId,
        Guid postId,
        int? commentSampleLimit,
        CancellationToken cancellationToken);
}
