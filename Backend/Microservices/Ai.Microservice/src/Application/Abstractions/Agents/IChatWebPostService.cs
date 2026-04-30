using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Agents;

public interface IChatWebPostService
{
    Task<Result<ChatWebPostResult>> CreateDraftAsync(
        ChatWebPostRequest request,
        CancellationToken cancellationToken);
}

public sealed record ChatWebPostRequest(
    Guid UserId,
    Guid SessionId,
    Guid WorkspaceId,
    string Prompt,
    string? SuggestedTitle = null,
    string? SuggestedPostType = null,
    Guid? OriginChatId = null);

public sealed record ChatWebPostResult(
    Guid PostId,
    string? Title,
    string RetrievalMode,
    IReadOnlyList<string> SourceUrls,
    IReadOnlyList<Guid> ImportedResourceIds);
