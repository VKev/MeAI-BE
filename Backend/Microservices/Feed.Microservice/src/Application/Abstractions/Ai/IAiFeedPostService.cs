using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Ai;

public interface IAiFeedPostService
{
    Task<Result<AiFeedMirrorPostResult>> CreateMirrorPostAsync(
        CreateAiMirrorPostRequest request,
        CancellationToken cancellationToken);

    Task<Result<bool>> DeleteMirrorPostAsync(
        DeleteAiMirrorPostRequest request,
        CancellationToken cancellationToken);
}

public sealed record CreateAiMirrorPostRequest(
    Guid UserId,
    Guid? WorkspaceId,
    Guid? SocialMediaId,
    string? Title,
    string? Content,
    string? HashtagText,
    IReadOnlyList<Guid> ResourceIds,
    string? PostType,
    string? Status);

public sealed record AiFeedMirrorPostResult(
    Guid PostId,
    DateTime? CreatedAt);

public sealed record DeleteAiMirrorPostRequest(
    Guid UserId,
    Guid PostId);
