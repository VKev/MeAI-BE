using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Workspaces.Commands;

public sealed record DeleteWorkspaceCommand(Guid WorkspaceId, Guid UserId) : IRequest<Result<bool>>;

public sealed class DeleteWorkspaceCommandHandler : IRequestHandler<DeleteWorkspaceCommand, Result<bool>>
{
    private readonly IRepository<Workspace> _repository;

    public DeleteWorkspaceCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Workspace>();
    }

    public async Task<Result<bool>> Handle(DeleteWorkspaceCommand request, CancellationToken cancellationToken)
    {
        var workspace = await _repository.GetAll()
            .FirstOrDefaultAsync(item =>
                    item.Id == request.WorkspaceId &&
                    item.UserId == request.UserId &&
                    !item.IsDeleted,
                cancellationToken);

        if (workspace == null)
        {
            return Result.Failure<bool>(new Error("Workspace.NotFound", "Workspace not found"));
        }

        workspace.DeletedAt = DateTimeExtensions.PostgreSqlUtcNow;
        workspace.IsDeleted = true;
        workspace.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _repository.Update(workspace);

        return Result.Success(true);
    }
}
