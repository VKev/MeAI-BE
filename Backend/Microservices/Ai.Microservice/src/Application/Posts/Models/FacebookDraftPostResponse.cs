namespace Application.Posts.Models;

public sealed record FacebookDraftPostResponse(
    Guid PostId,
    Guid PostBuilderId,
    string Status,
    string PostType,
    string Caption,
    IReadOnlyList<Guid> ResourceIds,
    bool CaptionGenerated);
