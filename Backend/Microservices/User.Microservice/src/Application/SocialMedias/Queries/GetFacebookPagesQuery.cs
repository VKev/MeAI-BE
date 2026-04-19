using Application.Abstractions.Data;
using Application.Abstractions.SocialMedia;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.SocialMedias.Queries;

public sealed record GetFacebookPagesQuery(Guid UserId) : IRequest<Result<List<SocialMediaResponse>>>;

public sealed class GetFacebookPagesQueryHandler
    : IRequestHandler<GetFacebookPagesQuery, Result<List<SocialMediaResponse>>>
{
    private const string FacebookType = "facebook";
    private readonly IRepository<SocialMedia> _repository;
    private readonly ISocialMediaProfileService _profileService;

    public GetFacebookPagesQueryHandler(
        IUnitOfWork unitOfWork,
        ISocialMediaProfileService profileService)
    {
        _repository = unitOfWork.Repository<SocialMedia>();
        _profileService = profileService;
    }

    public async Task<Result<List<SocialMediaResponse>>> Handle(
        GetFacebookPagesQuery request,
        CancellationToken cancellationToken)
    {
        var facebookPages = await _repository.GetAll()
            .AsNoTracking()
            .Where(sm =>
                sm.UserId == request.UserId &&
                sm.Type == FacebookType &&
                !sm.IsDeleted)
            .OrderByDescending(sm => sm.CreatedAt)
            .ThenByDescending(sm => sm.Id)
            .ToListAsync(cancellationToken);

        var responses = await Task.WhenAll(
            facebookPages.Select(async page =>
            {
                var profileResult = await _profileService.GetUserProfileAsync(
                    page.Type,
                    page.Metadata,
                    cancellationToken);

                var profile = profileResult.IsSuccess ? profileResult.Value : null;
                return SocialMediaMapping.ToResponse(page, profile);
            }));

        return Result.Success(responses.ToList());
    }
}
