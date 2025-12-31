using Application.Abstractions.Data;
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

    public GetSocialMediaByIdQueryHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<SocialMedia>();
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

        return Result.Success(SocialMediaMapping.ToResponse(socialMedia));
    }
}
