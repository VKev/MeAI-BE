using System.Text;
using System.Text.Json;
using Application.Abstractions.Automation;
using Application.PublishingSchedules.Models;

namespace Application.PublishingSchedules;

public static class AgenticScheduleExecutionContextSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(AgenticScheduleExecutionContext context)
    {
        return JsonSerializer.Serialize(Sanitize(context), JsonOptions);
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

    private static AgenticScheduleExecutionContext Sanitize(AgenticScheduleExecutionContext context)
    {
        return context with
        {
            Search = Sanitize(context.Search),
            DesiredPostType = SanitizeJsonbString(context.DesiredPostType),
            LastQuery = SanitizeJsonbString(context.LastQuery),
            LastRecommendationQuery = SanitizeJsonbString(context.LastRecommendationQuery),
            LastRecommendationSummary = SanitizeJsonbString(context.LastRecommendationSummary),
            LastRagFallbackReason = SanitizeJsonbString(context.LastRagFallbackReason),
            LastSearchPayload = Sanitize(context.LastSearchPayload),
            CurrentStep = SanitizeJsonbString(context.CurrentStep),
            CurrentStepStatus = SanitizeJsonbString(context.CurrentStepStatus),
            CurrentStepMessage = SanitizeJsonbString(context.CurrentStepMessage),
            Steps = context.Steps?.Select(Sanitize).ToList()
        };
    }

    private static PublishingScheduleSearchInput? Sanitize(PublishingScheduleSearchInput? search)
    {
        return search is null
            ? null
            : search with
            {
                QueryTemplate = SanitizeJsonbString(search.QueryTemplate),
                Country = SanitizeJsonbString(search.Country),
                SearchLanguage = SanitizeJsonbString(search.SearchLanguage),
                Freshness = SanitizeJsonbString(search.Freshness)
            };
    }

    private static AgentWebSearchResponse? Sanitize(AgentWebSearchResponse? response)
    {
        return response is null
            ? null
            : response with
            {
                Query = SanitizeJsonbString(response.Query) ?? string.Empty,
                Results = response.Results.Select(Sanitize).ToList(),
                LlmContext = SanitizeJsonbString(response.LlmContext),
                ImportedResources = response.ImportedResources?.Select(Sanitize).ToList()
            };
    }

    private static AgentWebSearchResultItem Sanitize(AgentWebSearchResultItem item)
    {
        return item with
        {
            Title = SanitizeJsonbString(item.Title),
            Url = SanitizeJsonbString(item.Url),
            Description = SanitizeJsonbString(item.Description),
            Source = SanitizeJsonbString(item.Source),
            PageTitle = SanitizeJsonbString(item.PageTitle),
            PageContent = SanitizeJsonbString(item.PageContent),
            MediaUrls = item.MediaUrls?.Select(SanitizeJsonbString)
                .Where(value => value is not null)
                .Select(value => value!)
                .ToList()
        };
    }

    private static ImportedResourceItem Sanitize(ImportedResourceItem item)
    {
        return item with
        {
            PresignedUrl = SanitizeJsonbString(item.PresignedUrl) ?? string.Empty,
            ContentType = SanitizeJsonbString(item.ContentType),
            ResourceType = SanitizeJsonbString(item.ResourceType),
            SourceUrl = SanitizeJsonbString(item.SourceUrl) ?? string.Empty,
            SourcePageUrl = SanitizeJsonbString(item.SourcePageUrl)
        };
    }

    private static AgenticExecutionProgressLog Sanitize(AgenticExecutionProgressLog log)
    {
        return log with
        {
            Step = SanitizeJsonbString(log.Step) ?? string.Empty,
            Status = SanitizeJsonbString(log.Status) ?? string.Empty,
            Message = SanitizeJsonbString(log.Message) ?? string.Empty
        };
    }

    private static string? SanitizeJsonbString(string? value)
    {
        if (value is null)
        {
            return null;
        }

        StringBuilder? builder = null;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (!IsUnsupportedJsonbCharacter(character) && !IsInvalidSurrogate(value, index))
            {
                builder?.Append(character);
                continue;
            }

            builder ??= new StringBuilder(value.Length).Append(value, 0, index);
            builder.Append(' ');
        }

        return builder?.ToString() ?? value;
    }

    private static bool IsUnsupportedJsonbCharacter(char character)
    {
        return character == '\0' ||
               (char.IsControl(character) && character is not '\t' and not '\n' and not '\r');
    }

    private static bool IsInvalidSurrogate(string value, int index)
    {
        var character = value[index];
        if (!char.IsSurrogate(character))
        {
            return false;
        }

        if (char.IsHighSurrogate(character))
        {
            return index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]);
        }

        return index == 0 || !char.IsHighSurrogate(value[index - 1]);
    }
}

public sealed record AgenticExecutionProgressLog(
    string Step,
    string Status,
    string Message,
    DateTime TimestampUtc);

public sealed record AgenticScheduleExecutionContext(
    PublishingScheduleSearchInput? Search = null,
    string? DesiredPostType = null,
    Guid? LastExecutionRunId = null,
    Guid? RuntimePostId = null,
    Guid? RuntimePostBuilderId = null,
    IReadOnlyList<Guid>? RuntimePostIds = null,
    string? LastQuery = null,
    Guid? GroundingSocialMediaId = null,
    string? LastRecommendationQuery = null,
    string? LastRecommendationSummary = null,
    string? LastRagFallbackReason = null,
    DateTime? LastRetrievedAtUtc = null,
    DateTime? RegisteredAtUtc = null,
    DateTime? LastExecutionStartedAtUtc = null,
    AgentWebSearchResponse? LastSearchPayload = null,
    string? CurrentStep = null,
    string? CurrentStepStatus = null,
    string? CurrentStepMessage = null,
    IReadOnlyList<AgenticExecutionProgressLog>? Steps = null);
