using System.Text.Json;
using Application.Abstractions.Automation;
using Application.PublishingSchedules.Models;

namespace Application.PublishingSchedules;

public static class AgenticScheduleExecutionContextSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(AgenticScheduleExecutionContext context)
    {
        return JsonSerializer.Serialize(context, JsonOptions);
    }

    public static AgenticScheduleExecutionContext Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AgenticScheduleExecutionContext();
        }

        try
        {
            return JsonSerializer.Deserialize<AgenticScheduleExecutionContext>(json, JsonOptions)
                   ?? new AgenticScheduleExecutionContext();
        }
        catch (JsonException)
        {
            return new AgenticScheduleExecutionContext();
        }
    }
}

public sealed record AgenticScheduleExecutionContext(
    PublishingScheduleSearchInput? Search = null,
    Guid? LastExecutionRunId = null,
    Guid? RuntimePostId = null,
    string? LastQuery = null,
    Guid? GroundingSocialMediaId = null,
    string? LastRecommendationQuery = null,
    string? LastRecommendationSummary = null,
    string? LastRagFallbackReason = null,
    DateTime? LastRetrievedAtUtc = null,
    DateTime? RegisteredAtUtc = null,
    DateTime? LastExecutionStartedAtUtc = null,
    AgentWebSearchResponse? LastSearchPayload = null);
