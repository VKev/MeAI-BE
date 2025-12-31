using Application.Abstractions.Data;
using Application.Workspaces.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Workspaces.Commands;

public sealed record UpdateWorkspaceCommand(
    Guid WorkspaceId,
    Guid UserId,
    string Name,
    string? Type,
    string? Description) : IRequest<Result<WorkspaceResponse>>;

public sealed class UpdateWorkspaceCommandHandler
    : IRequestHandler<UpdateWorkspaceCommand, Result<WorkspaceResponse>>
{
    private readonly IRepository<Workspace> _repository;

    public UpdateWorkspaceCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Workspace>();
    }

    public async Task<Result<WorkspaceResponse>> Handle(UpdateWorkspaceCommand request,
        CancellationToken cancellationToken)
    {
        var workspace = await _repository.GetAll()
            .FirstOrDefaultAsync(item =>
                    item.Id == request.WorkspaceId &&
                    item.UserId == request.UserId &&
                    !item.IsDeleted,
                cancellationToken);

        if (workspace == null)
        {
            return Result.Failure<WorkspaceResponse>(new Error("Workspace.NotFound", "Workspace not found"));
        }

        workspace.Name = request.Name.Trim();
        workspace.Type = string.IsNullOrWhiteSpace(request.Type) ? null : request.Type.Trim();
        workspace.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        workspace.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        _repository.Update(workspace);

        return Result.Success(WorkspaceMapping.ToResponse(workspace));
    }
}
