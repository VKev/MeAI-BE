namespace Application.PublishingSchedules;

public static class PublishingScheduleState
{
    public const string FixedContentMode = "fixed_content";
    public const string AgenticMode = "agentic";
    public const string CreatedByUser = "user";
    public const string StatusScheduled = "scheduled";
    public const string StatusWaitingForExecution = "waiting_for_execution";
    public const string StatusExecuting = "executing";
    public const string StatusPublishing = "publishing";
    public const string StatusCompleted = "completed";
    public const string StatusFailed = "failed";
    public const string StatusNeedsUserAction = "needs_user_action";
    public const string StatusCancelled = "cancelled";
    public const string ItemTypePost = "post";
    public const string ExecutionBehaviorPublishAll = "publish_all";
    public const string ItemStatusScheduled = "scheduled";
    public const string ItemStatusPublishing = "publishing";
    public const string ItemStatusPublished = "published";
    public const string ItemStatusFailed = "failed";
    public const string ItemStatusCancelled = "cancelled";
}
