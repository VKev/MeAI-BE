using System.Text.Json;
using Application.Abstractions.Data;
using Application.Abstractions.SocialMedia;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.SocialMedias.Commands;

public sealed record UpdateSocialMediaCommand(
    Guid SocialMediaId,
    Guid UserId,
    string Type,
    JsonDocument? Metadata) : IRequest<Result<SocialMediaResponse>>;

public sealed class UpdateSocialMediaCommandHandler
    : IRequestHandler<UpdateSocialMediaCommand, Result<SocialMediaResponse>>
{
    private readonly IRepository<SocialMedia> _repository;
    private readonly ISocialMediaProfileService _profileService;

    public UpdateSocialMediaCommandHandler(IUnitOfWork unitOfWork, ISocialMediaProfileService profileService)
    {
        _repository = unitOfWork.Repository<SocialMedia>();
        _profileService = profileService;
    }

    public async Task<Result<SocialMediaResponse>> Handle(UpdateSocialMediaCommand request,
        CancellationToken cancellationToken)
    {
        var socialMedia = await _repository.GetAll()
            .FirstOrDefaultAsync(item =>
                    item.Id == request.SocialMediaId &&
                    item.UserId == request.UserId &&
                    !item.IsDeleted,
                cancellationToken);

        if (socialMedia == null)
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("SocialMedia.NotFound", "Social media not found"));
        }

        socialMedia.Type = request.Type.Trim();
        socialMedia.Metadata = request.Metadata;
        socialMedia.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        _repository.Update(socialMedia);

        var profileResult = await _profileService.GetUserProfileAsync(
            socialMedia.Type,
            socialMedia.Metadata,
            cancellationToken);

        var profile = profileResult.IsSuccess ? profileResult.Value : null;
        return Result.Success(SocialMediaMapping.ToResponse(socialMedia, profile));
    }
}
