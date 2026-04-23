namespace Application.PublishingSchedules.Models;

public sealed record PublishingScheduleResponse(
    Guid Id,
    Guid UserId,
    Guid WorkspaceId,
    string? Name,
    string? Mode,
    string? Status,
    DateTime ExecuteAtUtc,
    string? Timezone,
    bool? IsPrivate,
    string? CreatedBy,
    string? PlatformPreference,
    string? AgentPrompt,
    string? ExecutionContextJson,
    IReadOnlyList<PublishingScheduleItemResponse> Items,
    IReadOnlyList<PublishingScheduleTargetResponse> Targets,
    DateTime? LastExecutionAt,
    DateTime? NextRetryAt,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);

public sealed record PublishingScheduleItemResponse(
    Guid Id,
    string? ItemType,
    Guid ItemId,
    int SortOrder,
    string? ExecutionBehavior,
    string? Status,
    string? ErrorMessage,
    DateTime? LastExecutionAt,
    string? ItemTitle,
    string? ItemCurrentStatus);

public sealed record PublishingScheduleTargetResponse(
    Guid Id,
    Guid SocialMediaId,
    string? Platform,
    string? TargetLabel,
    bool IsPrimary);

public sealed record PublishingScheduleItemInput(
    string? ItemType,
    Guid ItemId,
    int? SortOrder,
    string? ExecutionBehavior);

public sealed record PublishingScheduleTargetInput(
    Guid SocialMediaId,
    bool? IsPrimary = null);

public sealed record PublishingScheduleSearchInput(
    string? QueryTemplate,
    int? Count,
    string? Country,
    string? SearchLanguage,
    string? Freshness);
