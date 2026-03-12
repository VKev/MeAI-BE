using SharedLibrary.Common.ResponseModel;

namespace Application.Kie;

public static class KieErrors
{
    public static readonly Error InvalidCorrelationId = new("Kie.InvalidCorrelationId", "Valid Correlation ID is required");

    public static readonly Error TaskNotFound = new("Kie.TaskNotFound", "Image task not found in database");

    public static readonly Error TaskNotCompleted = new("Kie.TaskNotCompleted", "Image task has not completed yet. KieTaskId is not available.");

    public static readonly Error Unauthorized = new("Kie.Unauthorized", "You are not authorized to access this image task");

    public static Error ApiError(int code, string message) => new($"Kie.ApiError.{code}", message);
}
