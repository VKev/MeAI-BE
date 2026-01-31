namespace Application.Posts.Models;

public sealed record FacebookDraftPostResponse(
    Guid PostId,
    string Status,
    string PostType,
    string Caption,
    IReadOnlyList<Guid> ResourceIds,
    bool CaptionGenerated);
