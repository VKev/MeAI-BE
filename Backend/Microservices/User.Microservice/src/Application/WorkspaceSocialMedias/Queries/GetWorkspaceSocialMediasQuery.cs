using Application.Abstractions.Data;
using Application.SocialMedias;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.WorkspaceSocialMedias.Queries;

public sealed record GetWorkspaceSocialMediasQuery(
    Guid WorkspaceId,
    Guid UserId,
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int? Limit) : IRequest<Result<List<SocialMediaResponse>>>;

public sealed class GetWorkspaceSocialMediasQueryHandler
    : IRequestHandler<GetWorkspaceSocialMediasQuery, Result<List<SocialMediaResponse>>>
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;
    private readonly IRepository<Workspace> _workspaceRepository;
    private readonly IRepository<WorkspaceSocialMedia> _linkRepository;
    private readonly IRepository<SocialMedia> _socialMediaRepository;

    public GetWorkspaceSocialMediasQueryHandler(IUnitOfWork unitOfWork)
    {
        _workspaceRepository = unitOfWork.Repository<Workspace>();
        _linkRepository = unitOfWork.Repository<WorkspaceSocialMedia>();
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
    }

    public async Task<Result<List<SocialMediaResponse>>> Handle(GetWorkspaceSocialMediasQuery request,
        CancellationToken cancellationToken)
    {
        var workspaceExists = await _workspaceRepository.GetAll()
            .AsNoTracking()
            .AnyAsync(item =>
                    item.Id == request.WorkspaceId &&
                    item.UserId == request.UserId &&
                    !item.IsDeleted,
                cancellationToken);

        if (!workspaceExists)
        {
            return Result.Failure<List<SocialMediaResponse>>(
                new Error("Workspace.NotFound", "Workspace not found"));
        }

        var pageSize = Math.Clamp(request.Limit ?? DefaultPageSize, 1, MaxPageSize);

        var links = _linkRepository.GetAll()
            .AsNoTracking()
            .Where(link => link.WorkspaceId == request.WorkspaceId &&
                           link.UserId == request.UserId &&
                           !link.IsDeleted);

        var socialMedias = _socialMediaRepository.GetAll()
            .AsNoTracking()
            .Where(social => social.UserId == request.UserId && !social.IsDeleted);

        var query = links.Join(
            socialMedias,
            link => link.SocialMediaId,
            social => social.Id,
            (_, social) => social);

        if (request.CursorCreatedAt.HasValue && request.CursorId.HasValue)
        {
            var createdAt = request.CursorCreatedAt.Value;
            var lastId = request.CursorId.Value;
            query = query.Where(social =>
                (social.CreatedAt < createdAt) ||
                (social.CreatedAt == createdAt && social.Id.CompareTo(lastId) < 0));
        }

        var results = await query
            .OrderByDescending(social => social.CreatedAt)
            .ThenByDescending(social => social.Id)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var response = results.Select(SocialMediaMapping.ToResponse).ToList();
        return Result.Success(response);
    }
}
