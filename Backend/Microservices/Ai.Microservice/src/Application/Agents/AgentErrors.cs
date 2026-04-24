using SharedLibrary.Common.ResponseModel;

namespace Application.Agents;

public static class AgentErrors
{
    public static readonly Error InvalidMessage = new(
        "Agent.InvalidMessage",
        "Message is required.");

    public static readonly Error EmptyResponse = new(
        "Agent.EmptyResponse",
        "Gemini did not return a response.");

    public static readonly Error DuplicateMessageInProgress = new(
        "Agent.DuplicateMessageInProgress",
        "This message is already being processed.");
}
