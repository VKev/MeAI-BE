using Application.Posts.Models;

namespace Application.Posts;

internal static class SocialPlatformPostAnalysisFactory
{
    public static SocialPlatformPostAnalysisResponse Create(SocialPlatformPostStatsResponse stats)
    {
        var conversationInteractions = (stats.Comments ?? 0) + (stats.Replies ?? 0);
        var amplificationInteractions = (stats.Shares ?? 0) + (stats.Reposts ?? 0) + (stats.Quotes ?? 0);
        var approvalInteractions = stats.Likes ?? 0;

        var engagementRate = CalculateRate(stats.TotalInteractions, stats.Views);
        var conversationRate = CalculateRate(conversationInteractions, stats.Views);
        var amplificationRate = CalculateRate(amplificationInteractions, stats.Views);
        var approvalRate = CalculateRate(approvalInteractions, stats.Views);

        var highlights = BuildHighlights(
            stats.Views,
            stats.TotalInteractions,
            engagementRate,
            conversationRate,
            amplificationRate,
            approvalRate);

        return new SocialPlatformPostAnalysisResponse(
            EngagementRateByViews: engagementRate,
            ConversationRateByViews: conversationRate,
            AmplificationRateByViews: amplificationRate,
            ApprovalRateByViews: approvalRate,
            PerformanceBand: ResolvePerformanceBand(engagementRate),
            Highlights: highlights);
    }

    private static decimal? CalculateRate(long numerator, long? denominator)
    {
        if (denominator is null || denominator <= 0)
        {
            return null;
        }

        return Math.Round((decimal)numerator / denominator.Value * 100m, 2);
    }

    private static IReadOnlyList<string> BuildHighlights(
        long? views,
        long totalInteractions,
        decimal? engagementRate,
        decimal? conversationRate,
        decimal? amplificationRate,
        decimal? approvalRate)
    {
        var highlights = new List<string>();

        if (views is null || views <= 0)
        {
            highlights.Add("Provider response does not expose enough view data for rate-based analysis.");
            if (totalInteractions > 0)
            {
                highlights.Add($"The post still recorded {totalInteractions} tracked interactions.");
            }

            return highlights;
        }

        highlights.Add($"Tracked engagement rate by views is {engagementRate:0.##}%.");

        if (conversationRate >= 1m)
        {
            highlights.Add($"Conversation is strong at {conversationRate:0.##}% of views.");
        }

        if (amplificationRate >= 0.5m)
        {
            highlights.Add($"Distribution signal is healthy at {amplificationRate:0.##}%.");
        }

        if (approvalRate >= 2m)
        {
            highlights.Add($"Approval signal is healthy at {approvalRate:0.##}%.");
        }

        if (highlights.Count == 1 && totalInteractions == 0)
        {
            highlights.Add("The post has not recorded tracked interactions yet.");
        }
        else if (highlights.Count == 1)
        {
            highlights.Add("Interaction volume is present, but no single engagement signal is dominant.");
        }

        return highlights;
    }

    private static string ResolvePerformanceBand(decimal? engagementRate)
    {
        if (engagementRate is null)
        {
            return "insufficient_data";
        }

        if (engagementRate >= 10m)
        {
            return "very_high";
        }

        if (engagementRate >= 5m)
        {
            return "strong";
        }

        if (engagementRate >= 2m)
        {
            return "moderate";
        }

        return "early_or_low";
    }
}
