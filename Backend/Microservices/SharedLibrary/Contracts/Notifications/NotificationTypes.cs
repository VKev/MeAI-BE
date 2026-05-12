namespace SharedLibrary.Contracts.Notifications;

public static class NotificationTypes
{
    public const string AiImageGenerationSubmitted = "ai.image_generation.submitted";
    public const string AiImageGenerationCompleted = "ai.image_generation.completed";
    public const string AiImageGenerationFailed = "ai.image_generation.failed";

    public const string AiVideoGenerationSubmitted = "ai.video_generation.submitted";
    public const string AiVideoGenerationCompleted = "ai.video_generation.completed";
    public const string AiVideoGenerationFailed = "ai.video_generation.failed";

    public const string AiVideoExtensionSubmitted = "ai.video_extension.submitted";

    public const string AiDraftPostGenerationSubmitted = "ai.draft_post_generation.submitted";
    public const string AiDraftPostGenerationThinking = "ai.draft_post_generation.thinking";
    public const string AiDraftPostGenerationCompleted = "ai.draft_post_generation.completed";
    public const string AiDraftPostGenerationFailed = "ai.draft_post_generation.failed";

    public const string AiPostImproveSubmitted = "ai.post_improve.submitted";
    public const string AiPostImproveProcessing = "ai.post_improve.processing";
    public const string AiPostImproveCompleted = "ai.post_improve.completed";
    public const string AiPostImproveFailed = "ai.post_improve.failed";

    public const string UserSubscriptionActivated = "user.subscription.activated";
    public const string UserSubscriptionRenewed = "user.subscription.renewed";
    public const string UserSubscriptionStatusChanged = "user.subscription.status_changed";
    public const string UserSubscriptionAutoRenewChanged = "user.subscription.auto_renew_changed";

    public const string PostPublishTargetSubmitted = "post.publish.target_submitted";
    public const string PostPublishTargetCompleted = "post.publish.target_completed";
    public const string PostPublishTargetFailed = "post.publish.target_failed";
    public const string PostPublishTargetRolledBack = "post.publish.target_rolled_back";
    public const string PostPublishBatchCompleted = "post.publish.batch_completed";

    public const string PostUnpublishTargetCompleted = "post.unpublish.target_completed";
    public const string PostUnpublishTargetFailed = "post.unpublish.target_failed";
    public const string PostUnpublishBatchCompleted = "post.unpublish.batch_completed";

    public const string PostUpdateTargetCompleted = "post.update.target_completed";
    public const string PostUpdateTargetFailed = "post.update.target_failed";
    public const string PostUpdateBatchCompleted = "post.update.batch_completed";
}
