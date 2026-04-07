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

    public const string UserSubscriptionActivated = "user.subscription.activated";
    public const string UserSubscriptionRenewed = "user.subscription.renewed";
}
