using System.Text.Json;
using Application.Posts.Models;
using Domain.Entities;
using SharedLibrary.Extensions;

namespace Application.Posts;

internal static class SocialPlatformAnalyticsSnapshotMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static SocialPlatformPostAnalyticsResponse? ToResponse(PostAnalyticsSnapshot? snapshot)
    {
        if (snapshot == null)
        {
            return null;
        }

        var post = Deserialize<SocialPlatformPostSummaryResponse>(snapshot.PostPayloadJson);
        var stats = Deserialize<SocialPlatformPostStatsResponse>(snapshot.StatsPayloadJson);
        var analysis = Deserialize<SocialPlatformPostAnalysisResponse>(snapshot.AnalysisPayloadJson);

        if (post == null || stats == null || analysis == null)
        {
            return null;
        }

        return new SocialPlatformPostAnalyticsResponse(
            SocialMediaId: snapshot.SocialMediaId,
            Platform: snapshot.Platform,
            PlatformPostId: snapshot.PlatformPostId,
            Post: post,
            Stats: stats,
            Analysis: analysis,
            RetrievedAt: new DateTimeOffset(DateTime.SpecifyKind(snapshot.RetrievedAt, DateTimeKind.Utc)));
    }

    public static void Apply(
        PostAnalyticsSnapshot snapshot,
        SocialPlatformPostAnalyticsResponse response)
    {
        snapshot.Platform = response.Platform;
        snapshot.PlatformPostId = response.PlatformPostId;
        snapshot.PostPayloadJson = JsonSerializer.Serialize(response.Post, JsonOptions);
        snapshot.StatsPayloadJson = JsonSerializer.Serialize(response.Stats, JsonOptions);
        snapshot.AnalysisPayloadJson = JsonSerializer.Serialize(response.Analysis, JsonOptions);
        snapshot.RetrievedAt = response.RetrievedAt.UtcDateTime;
        snapshot.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
    }

    public static PostAnalyticsSnapshot Create(
        Guid userId,
        Guid socialMediaId,
        string platform,
        string platformPostId,
        SocialPlatformPostAnalyticsResponse response)
    {
        var now = DateTimeExtensions.PostgreSqlUtcNow;

        return new PostAnalyticsSnapshot
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            SocialMediaId = socialMediaId,
            Platform = platform,
            PlatformPostId = platformPostId,
            PostPayloadJson = JsonSerializer.Serialize(response.Post, JsonOptions),
            StatsPayloadJson = JsonSerializer.Serialize(response.Stats, JsonOptions),
            AnalysisPayloadJson = JsonSerializer.Serialize(response.Analysis, JsonOptions),
            RetrievedAt = response.RetrievedAt.UtcDateTime,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static IReadOnlyDictionary<string, SocialPlatformPostStatsResponse> ToStatsLookup(
        IReadOnlyList<PostAnalyticsSnapshot> snapshots)
    {
        var result = new Dictionary<string, SocialPlatformPostStatsResponse>(StringComparer.Ordinal);

        foreach (var snapshot in snapshots)
        {
            var stats = Deserialize<SocialPlatformPostStatsResponse>(snapshot.StatsPayloadJson);
            if (stats == null || string.IsNullOrWhiteSpace(snapshot.PlatformPostId))
            {
                continue;
            }

            result[snapshot.PlatformPostId] = stats;
        }

        return result;
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
