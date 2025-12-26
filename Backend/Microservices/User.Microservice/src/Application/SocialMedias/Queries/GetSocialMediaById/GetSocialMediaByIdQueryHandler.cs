using Application.SocialMedias.Contracts;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.SocialMedias.Queries.GetSocialMediaById;

internal sealed class GetSocialMediaByIdQueryHandler(ISocialMediaRepository socialMediaRepository)
    : IQueryHandler<GetSocialMediaByIdQuery, SocialMediaResponse>
{
    public async Task<Result<SocialMediaResponse>> Handle(GetSocialMediaByIdQuery request,
        CancellationToken cancellationToken)
    {
        var socialMedia = await socialMediaRepository.GetByIdForUserAsync(
            request.SocialMediaId,
            request.UserId,
            cancellationToken);

        if (socialMedia == null)
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("SocialMedia.NotFound", "Social media not found"));
        }

        return Result.Success(SocialMediaMapping.ToResponse(socialMedia));
    }
}
