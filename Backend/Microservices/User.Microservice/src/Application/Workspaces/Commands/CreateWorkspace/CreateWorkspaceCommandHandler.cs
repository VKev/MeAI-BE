using Application.Workspaces.Contracts;
using Domain.Entities;
using Domain.Repositories;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Workspaces.Commands.CreateWorkspace;

internal sealed class CreateWorkspaceCommandHandler(IWorkspaceRepository workspaceRepository)
    : ICommandHandler<CreateWorkspaceCommand, WorkspaceResponse>
{
    public async Task<Result<WorkspaceResponse>> Handle(CreateWorkspaceCommand request,
        CancellationToken cancellationToken)
    {
        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            Name = request.Name.Trim(),
            Type = string.IsNullOrWhiteSpace(request.Type) ? null : request.Type.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await workspaceRepository.AddAsync(workspace, cancellationToken);

        return Result.Success(WorkspaceMapping.ToResponse(workspace));
    }
}
