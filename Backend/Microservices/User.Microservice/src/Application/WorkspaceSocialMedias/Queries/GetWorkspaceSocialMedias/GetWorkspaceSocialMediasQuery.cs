using Application.SocialMedias.Contracts;
using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.WorkspaceSocialMedias.Queries.GetWorkspaceSocialMedias;

public sealed record GetWorkspaceSocialMediasQuery(
    [Required] Guid WorkspaceId,
    [Required] Guid UserId)
    : IQuery<IReadOnlyList<SocialMediaResponse>>;
