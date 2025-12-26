using Application.SocialMedias.Contracts;
using System.Linq;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.SocialMedias.Queries.GetSocialMedias;

internal sealed class GetSocialMediasQueryHandler(ISocialMediaRepository socialMediaRepository)
    : IQueryHandler<GetSocialMediasQuery, IReadOnlyList<SocialMediaResponse>>
{
    public async Task<Result<IReadOnlyList<SocialMediaResponse>>> Handle(GetSocialMediasQuery request,
        CancellationToken cancellationToken)
    {
        var socialMedias = await socialMediaRepository.GetForUserAsync(request.UserId, cancellationToken);
        var response = socialMedias.Select(SocialMediaMapping.ToResponse).ToList();
        return Result.Success<IReadOnlyList<SocialMediaResponse>>(response);
    }
}
