using SharedLibrary.Common.ResponseModel;

namespace Application.PublishingSchedules;

public static class PublishingScheduleErrors
{
    public static readonly Error NotFound = new("PublishingSchedule.NotFound", "Publishing schedule not found");
    public static readonly Error Unauthorized = new("PublishingSchedule.Unauthorized", "You are not authorized to access this publishing schedule");
    public static readonly Error WorkspaceNotFound = new("PublishingSchedule.WorkspaceNotFound", "Workspace not found");
    public static readonly Error ExecuteAtInPast = new("PublishingSchedule.ExecuteAtInPast", "ExecuteAtUtc must be in the future");
    public static readonly Error InvalidTimezone = new("PublishingSchedule.InvalidTimezone", "Timezone is invalid");
    public static readonly Error NameRequired = new("PublishingSchedule.NameRequired", "Name is required");
    public static readonly Error UnsupportedMode = new("PublishingSchedule.UnsupportedMode", "Only fixed_content schedules are supported at the moment");
    public static readonly Error MissingItems = new("PublishingSchedule.MissingItems", "At least one schedule item is required");
    public static readonly Error MissingTargets = new("PublishingSchedule.MissingTargets", "At least one social media target is required");
    public static readonly Error UnsupportedItemType = new("PublishingSchedule.UnsupportedItemType", "Only post items are supported at the moment");
    public static readonly Error ScheduleAlreadyCancelled = new("PublishingSchedule.AlreadyCancelled", "This schedule is already cancelled");
    public static readonly Error ScheduleCannotActivate = new("PublishingSchedule.CannotActivate", "This schedule cannot be activated");
    public static readonly Error AgentPromptRequired = new("PublishingSchedule.AgentPromptRequired", "AgentPrompt is required for agentic schedules");
    public static readonly Error SearchConfigRequired = new("PublishingSchedule.SearchConfigRequired", "Search configuration is required for agentic schedules");
    public static readonly Error SearchQueryTemplateRequired = new("PublishingSchedule.SearchQueryTemplateRequired", "Search query template is required for agentic schedules");
    public static readonly Error MaxContentLengthRequired = new("PublishingSchedule.MaxContentLengthRequired", "MaxContentLength is required for agentic schedules");
    public static readonly Error InvalidMaxContentLength = new("PublishingSchedule.InvalidMaxContentLength", "MaxContentLength must be between 1 and 10000 characters");
    public static readonly Error UnsupportedModeForHandler = new("PublishingSchedule.UnsupportedModeForHandler", "This operation does not support the requested schedule mode");
    public static readonly Error InternalCallbackUnauthorized = new("PublishingSchedule.InternalCallbackUnauthorized", "Internal callback authorization failed");
}
