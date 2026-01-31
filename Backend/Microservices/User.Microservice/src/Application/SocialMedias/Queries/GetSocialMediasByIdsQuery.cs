using Application.Abstractions.Data;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.SocialMedias.Queries;

public sealed record GetSocialMediasByIdsQuery(
    Guid UserId,
    IReadOnlyList<Guid> SocialMediaIds) : IRequest<Result<IReadOnlyList<SocialMediaResponse>>>;

public sealed class GetSocialMediasByIdsQueryHandler
    : IRequestHandler<GetSocialMediasByIdsQuery, Result<IReadOnlyList<SocialMediaResponse>>>
{
    private readonly IRepository<SocialMedia> _socialMediaRepository;

    public GetSocialMediasByIdsQueryHandler(IUnitOfWork unitOfWork)
    {
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
    }

    public async Task<Result<IReadOnlyList<SocialMediaResponse>>> Handle(
        GetSocialMediasByIdsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.SocialMediaIds.Count == 0)
        {
            return Result.Failure<IReadOnlyList<SocialMediaResponse>>(
                new Error("SocialMedia.Missing", "At least one social media id is required."));
        }

        var ids = request.SocialMediaIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return Result.Failure<IReadOnlyList<SocialMediaResponse>>(
                new Error("SocialMedia.Missing", "At least one social media id is required."));
        }

        var socialMedias = await _socialMediaRepository.GetAll()
            .AsNoTracking()
            .Where(item => item.UserId == request.UserId &&
                           ids.Contains(item.Id) &&
                           !item.IsDeleted)
            .ToListAsync(cancellationToken);

        if (socialMedias.Count != ids.Count)
        {
            return Result.Failure<IReadOnlyList<SocialMediaResponse>>(
                new Error("SocialMedia.NotFound", "One or more social media accounts were not found."));
        }

        var response = socialMedias.Select(sm => SocialMediaMapping.ToResponse(sm)).ToList();
        return Result.Success<IReadOnlyList<SocialMediaResponse>>(response);
    }
}
