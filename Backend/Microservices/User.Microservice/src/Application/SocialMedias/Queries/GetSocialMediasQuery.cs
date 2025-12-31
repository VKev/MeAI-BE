using Application.Abstractions.Data;
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

    public GetSocialMediasQueryHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<SocialMedia>();
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

        var response = socialMedias.Select(SocialMediaMapping.ToResponse).ToList();
        return Result.Success(response);
    }
}
