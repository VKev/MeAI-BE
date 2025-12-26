using Application.SocialMedias.Contracts;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.SocialMedias.Commands.UpdateSocialMedia;

internal sealed class UpdateSocialMediaCommandHandler(ISocialMediaRepository socialMediaRepository)
    : ICommandHandler<UpdateSocialMediaCommand, SocialMediaResponse>
{
    public async Task<Result<SocialMediaResponse>> Handle(UpdateSocialMediaCommand request,
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

        socialMedia.Type = request.Type.Trim();
        socialMedia.Metadata = request.Metadata;
        socialMedia.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        socialMediaRepository.Update(socialMedia);

        return Result.Success(SocialMediaMapping.ToResponse(socialMedia));
    }
}
