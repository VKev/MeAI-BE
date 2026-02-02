using Application.Abstractions.Data;
using Application.Abstractions.SocialMedia;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.SocialMedias.Queries;

public sealed record GetSocialMediasQuery(
    Guid UserId,
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int? Limit) : IRequest<Result<List<SocialMediaResponse>>>;

public sealed class GetSocialMediasQueryHandler
    : IRequestHandler<GetSocialMediasQuery, Result<List<SocialMediaResponse>>>
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;
    private readonly IRepository<SocialMedia> _repository;
    private readonly ISocialMediaProfileService _profileService;

    public GetSocialMediasQueryHandler(IUnitOfWork unitOfWork, ISocialMediaProfileService profileService)
    {
        _repository = unitOfWork.Repository<SocialMedia>();
        _profileService = profileService;
    }

    public async Task<Result<List<SocialMediaResponse>>> Handle(GetSocialMediasQuery request,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.Limit ?? DefaultPageSize, 1, MaxPageSize);

        var query = _repository.GetAll()
            .AsNoTracking()
            .Where(sm => sm.UserId == request.UserId && !sm.IsDeleted);

        if (request.CursorCreatedAt.HasValue && request.CursorId.HasValue)
        {
            var createdAt = request.CursorCreatedAt.Value;
            var lastId = request.CursorId.Value;
            query = query.Where(sm =>
                (sm.CreatedAt < createdAt) ||
                (sm.CreatedAt == createdAt && sm.Id.CompareTo(lastId) < 0));
        }

        var socialMedias = await query
            .OrderByDescending(sm => sm.CreatedAt)
            .ThenByDescending(sm => sm.Id)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var responses = new List<SocialMediaResponse>();
        foreach (var socialMedia in socialMedias)
        {
            var profileResult = await _profileService.GetUserProfileAsync(
                socialMedia.Type,
                socialMedia.Metadata,
                cancellationToken);

            var profile = profileResult.IsSuccess ? profileResult.Value : null;
            responses.Add(SocialMediaMapping.ToResponse(socialMedia, profile));
        }

        return Result.Success(responses);
    }
}
