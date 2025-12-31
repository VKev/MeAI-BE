using Application.Abstractions.Data;
using Application.Workspaces.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Workspaces.Queries;

public sealed record GetWorkspaceByIdQuery(Guid WorkspaceId, Guid UserId)
    : IRequest<Result<WorkspaceResponse>>;

public sealed class GetWorkspaceByIdQueryHandler
    : IRequestHandler<GetWorkspaceByIdQuery, Result<WorkspaceResponse>>
{
    private readonly IRepository<Workspace> _repository;

    public GetWorkspaceByIdQueryHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Workspace>();
    }

    public async Task<Result<WorkspaceResponse>> Handle(GetWorkspaceByIdQuery request,
        CancellationToken cancellationToken)
    {
        var workspace = await _repository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(item =>
                    item.Id == request.WorkspaceId &&
                    item.UserId == request.UserId &&
                    !item.IsDeleted,
                cancellationToken);

        if (workspace == null)
        {
            return Result.Failure<WorkspaceResponse>(new Error("Workspace.NotFound", "Workspace not found"));
        }

        return Result.Success(WorkspaceMapping.ToResponse(workspace));
    }
}
