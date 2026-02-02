using Application.Abstractions.Data;
using Application.Abstractions.SocialMedia;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.SocialMedias.Queries;

public sealed record GetSocialMediaByIdQuery(Guid SocialMediaId, Guid UserId)
    : IRequest<Result<SocialMediaResponse>>;

public sealed class GetSocialMediaByIdQueryHandler
    : IRequestHandler<GetSocialMediaByIdQuery, Result<SocialMediaResponse>>
{
    private readonly IRepository<SocialMedia> _repository;
    private readonly ISocialMediaProfileService _profileService;

    public GetSocialMediaByIdQueryHandler(IUnitOfWork unitOfWork, ISocialMediaProfileService profileService)
    {
        _repository = unitOfWork.Repository<SocialMedia>();
        _profileService = profileService;
    }

    public async Task<Result<SocialMediaResponse>> Handle(GetSocialMediaByIdQuery request,
        CancellationToken cancellationToken)
    {
        var socialMedia = await _repository.GetAll()
            .AsNoTracking()
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

        var profileResult = await _profileService.GetUserProfileAsync(
            socialMedia.Type,
            socialMedia.Metadata,
            cancellationToken);

        var profile = profileResult.IsSuccess ? profileResult.Value : null;
        return Result.Success(SocialMediaMapping.ToResponse(socialMedia, profile));
    }
}
