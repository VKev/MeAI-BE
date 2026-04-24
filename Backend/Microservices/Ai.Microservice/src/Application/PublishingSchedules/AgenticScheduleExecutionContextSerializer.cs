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
    Guid? N8nJobId = null,
    string? N8nExecutionId = null,
    Guid? LastProcessedCallbackJobId = null,
    Guid? RuntimePostId = null,
    string? LastQuery = null,
    DateTime? LastRetrievedAtUtc = null,
    DateTime? RegisteredAtUtc = null,
    DateTime? LastCallbackReceivedAtUtc = null,
    N8nWebSearchResponse? LastSearchPayload = null);
