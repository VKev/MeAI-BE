using Application.Abstractions.Feed;
using Grpc.Core;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Grpc.FeedAnalytics;

namespace Infrastructure.Logic.Feed;

public sealed class FeedResourceUsageGrpcService : IFeedResourceUsageService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    private readonly FeedAnalyticsService.FeedAnalyticsServiceClient _client;

    public FeedResourceUsageGrpcService(FeedAnalyticsService.FeedAnalyticsServiceClient client)
    {
        _client = client;
    }

    public async Task<Result<IReadOnlySet<Guid>>> GetActiveResourceIdsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetActiveResourceIdsAsync(
                new GetActiveFeedResourceIdsRequest(),
                deadline: DateTime.UtcNow.Add(RequestTimeout),
                cancellationToken: cancellationToken);

            var resourceIds = response.ResourceIds
                .Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToHashSet();

            return Result.Success<IReadOnlySet<Guid>>(resourceIds);
        }
        catch (RpcException ex)
        {
            return Result.Failure<IReadOnlySet<Guid>>(
                new Error("Feed.ResourceUsageUnavailable", ex.Status.Detail));
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlySet<Guid>>(
                new Error("Feed.ResourceUsageUnavailable", ex.Message));
        }
    }
}
