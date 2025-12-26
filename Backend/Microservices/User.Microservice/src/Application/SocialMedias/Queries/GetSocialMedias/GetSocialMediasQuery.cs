using Application.SocialMedias.Contracts;
using SharedLibrary.Abstractions.Messaging;
using System.ComponentModel.DataAnnotations;

namespace Application.SocialMedias.Queries.GetSocialMedias;

public sealed record GetSocialMediasQuery(
    [Required] Guid UserId) : IQuery<IReadOnlyList<SocialMediaResponse>>;
