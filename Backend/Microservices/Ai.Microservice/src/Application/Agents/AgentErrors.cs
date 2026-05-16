using SharedLibrary.Common.ResponseModel;

namespace Application.Agents;

public static class AgentErrors
{
    public static readonly Error InvalidMessage = new(
        "Agent.InvalidMessage",
        "Message is required.");

    public static readonly Error EmptyResponse = new(
        "Agent.EmptyResponse",
        "AI provider did not return a response.");

    public static readonly Error DuplicateMessageInProgress = new(
        "Agent.DuplicateMessageInProgress",
        "This message is already being processed.");

    public static readonly Error TranscriptDisabled = new(
        "Agent.TranscriptDisabled",
        "Agent transcript is disabled. Send a new message to get a single-turn response.");

    public static readonly Error WebContentNotFound = new(
        "Agent.WebContentNotFound",
        "Could not retrieve enough web content to create a draft post.");
}
