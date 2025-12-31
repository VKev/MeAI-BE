using Application.Abstractions.Data;
using Application.Workspaces.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Workspaces.Queries;

public sealed record GetWorkspacesQuery(
    Guid UserId,
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int? Limit) : IRequest<Result<List<WorkspaceResponse>>>;

public sealed class GetWorkspacesQueryHandler
    : IRequestHandler<GetWorkspacesQuery, Result<List<WorkspaceResponse>>>
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;
    private readonly IRepository<Workspace> _repository;

    public GetWorkspacesQueryHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Workspace>();
    }

    public async Task<Result<List<WorkspaceResponse>>> Handle(GetWorkspacesQuery request,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.Limit ?? DefaultPageSize, 1, MaxPageSize);

        var query = _repository.GetAll()
            .AsNoTracking()
            .Where(workspace => workspace.UserId == request.UserId && !workspace.IsDeleted);

        if (request.CursorCreatedAt.HasValue && request.CursorId.HasValue)
        {
            var createdAt = request.CursorCreatedAt.Value;
            var lastId = request.CursorId.Value;
            query = query.Where(workspace =>
                (workspace.CreatedAt < createdAt) ||
                (workspace.CreatedAt == createdAt && workspace.Id.CompareTo(lastId) < 0));
        }

        var workspaces = await query
            .OrderByDescending(workspace => workspace.CreatedAt)
            .ThenByDescending(workspace => workspace.Id)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var response = workspaces.Select(WorkspaceMapping.ToResponse).ToList();
        return Result.Success(response);
    }
}
