using SharedLibrary.Common.ResponseModel;

namespace Application.Veo;

public static class VeoErrors
{
    public static readonly Error GenerationFailed = new("Veo.GenerationFailed", "Video generation request failed");

    public static readonly Error ApiKeyMissing = new("Veo.ApiKeyMissing", "Veo API key is not configured");

    public static readonly Error InvalidPrompt = new("Veo.InvalidPrompt", "Prompt is required for video generation");

    public static readonly Error InvalidCorrelationId = new("Veo.InvalidCorrelationId", "Valid Correlation ID is required");

    public static readonly Error TaskNotFound = new("Veo.TaskNotFound", "Video task not found in database");

    public static readonly Error TaskNotCompleted = new("Veo.TaskNotCompleted", "Video task has not completed yet. VeoTaskId is not available.");

    public static readonly Error Unauthorized = new("Veo.Unauthorized", "You are not authorized to access this video task");

    public static Error ApiError(int code, string message) => new($"Veo.ApiError.{code}", message);
}

