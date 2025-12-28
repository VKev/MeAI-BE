using Application.SocialMedias.Contracts;
using Domain.Entities;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.SocialMedias.Commands.CreateSocialMedia;

internal sealed class CreateSocialMediaCommandHandler(ISocialMediaRepository socialMediaRepository)
    : ICommandHandler<CreateSocialMediaCommand, SocialMediaResponse>
{
    public async Task<Result<SocialMediaResponse>> Handle(CreateSocialMediaCommand request,
        CancellationToken cancellationToken)
    {
        var socialMedia = new SocialMedia
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            Type = request.Type.Trim(),
            Metadata = request.Metadata,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await socialMediaRepository.AddAsync(socialMedia, cancellationToken);

        return Result.Success(SocialMediaMapping.ToResponse(socialMedia));
    }
}
