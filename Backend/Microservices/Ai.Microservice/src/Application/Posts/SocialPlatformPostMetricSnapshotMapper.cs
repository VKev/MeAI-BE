using System.Text.Json;
using Application.Posts.Models;
using Domain.Entities;
using SharedLibrary.Extensions;

namespace Application.Posts;

internal static class SocialPlatformPostMetricSnapshotMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static SocialPlatformPostAnalyticsResponse? ToAnalyticsResponse(PostMetricSnapshot? metric)
    {
        if (metric == null)
        {
            return null;
        }

        var post = Deserialize<SocialPlatformPostSummaryResponse>(metric.PostPayloadJson);
        if (post == null)
        {
            return null;
        }

        var stats = ToStats(metric);

        return new SocialPlatformPostAnalyticsResponse(
            SocialMediaId: metric.SocialMediaId,
            Platform: metric.Platform,
            PlatformPostId: metric.PlatformPostId,
            Post: post with { Stats = stats },
            Stats: stats,
            Analysis: SocialPlatformPostAnalysisFactory.Create(stats),
            RetrievedAt: new DateTimeOffset(DateTime.SpecifyKind(metric.RetrievedAt, DateTimeKind.Utc)));
    }

    public static void Apply(
        PostMetricSnapshot metric,
        SocialPlatformPostAnalyticsResponse response)
    {
        metric.Platform = response.Platform;
        metric.PlatformPostId = response.PlatformPostId;
        metric.PostPayloadJson = JsonSerializer.Serialize(response.Post with { Stats = response.Stats }, JsonOptions);
        ApplyStats(metric, response.Stats);
        metric.RawMetricsJson = JsonSerializer.Serialize(response.Stats, JsonOptions);
        metric.RetrievedAt = response.RetrievedAt.UtcDateTime;
        metric.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
    }

    public static PostMetricSnapshot Create(
        Guid userId,
        Guid socialMediaId,
        string platform,
        string platformPostId,
        SocialPlatformPostAnalyticsResponse response)
    {
        var now = DateTimeExtensions.PostgreSqlUtcNow;

        var metric = new PostMetricSnapshot
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            SocialMediaId = socialMediaId,
            Platform = platform,
            PlatformPostId = platformPostId,
            RetrievedAt = response.RetrievedAt.UtcDateTime,
            CreatedAt = now,
            UpdatedAt = now
        };

        Apply(metric, response);

        return metric;
    }

    public static IReadOnlyDictionary<string, SocialPlatformPostStatsResponse> ToStatsLookup(
        IReadOnlyList<PostMetricSnapshot> metrics)
    {
        var result = new Dictionary<string, SocialPlatformPostStatsResponse>(StringComparer.Ordinal);

        foreach (var metric in metrics)
        {
            if (string.IsNullOrWhiteSpace(metric.PlatformPostId))
            {
                continue;
            }

            result[metric.PlatformPostId] = ToStats(metric);
        }

        return result;
    }

    private static SocialPlatformPostStatsResponse ToStats(PostMetricSnapshot metric)
    {
        var totalInteractions =
            (metric.LikeCount ?? 0) +
            (metric.CommentCount ?? 0) +
            (metric.ReplyCount ?? 0) +
            (metric.ShareCount ?? 0) +
            (metric.RepostCount ?? 0) +
            (metric.QuoteCount ?? 0);

        return new SocialPlatformPostStatsResponse(
            Views: metric.ViewCount,
            Likes: metric.LikeCount,
            Comments: metric.CommentCount,
            Replies: metric.ReplyCount,
            Shares: metric.ShareCount,
            Reposts: metric.RepostCount,
            Quotes: metric.QuoteCount,
            TotalInteractions: totalInteractions);
    }

    private static void ApplyStats(PostMetricSnapshot metric, SocialPlatformPostStatsResponse stats)
    {
        metric.ViewCount = stats.Views;
        metric.LikeCount = stats.Likes;
        metric.CommentCount = stats.Comments;
        metric.ReplyCount = stats.Replies;
        metric.ShareCount = stats.Shares;
        metric.RepostCount = stats.Reposts;
        metric.QuoteCount = stats.Quotes;
    }

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
