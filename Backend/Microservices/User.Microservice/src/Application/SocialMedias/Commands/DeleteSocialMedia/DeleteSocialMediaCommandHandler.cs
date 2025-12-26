using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.SocialMedias.Commands.DeleteSocialMedia;

internal sealed class DeleteSocialMediaCommandHandler(ISocialMediaRepository socialMediaRepository)
    : ICommandHandler<DeleteSocialMediaCommand>
{
    public async Task<Result> Handle(DeleteSocialMediaCommand request, CancellationToken cancellationToken)
    {
        var socialMedia = await socialMediaRepository.GetByIdForUserAsync(
            request.SocialMediaId,
            request.UserId,
            cancellationToken);

        if (socialMedia == null)
        {
            return Result.Failure(new Error("SocialMedia.NotFound", "Social media not found"));
        }

        socialMedia.DeletedAt = DateTimeExtensions.PostgreSqlUtcNow;
        socialMedia.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        socialMediaRepository.Update(socialMedia);

        return Result.Success();
    }
}
