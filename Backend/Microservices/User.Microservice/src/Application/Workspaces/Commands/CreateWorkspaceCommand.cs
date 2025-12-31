using Application.Abstractions.Data;
using Application.Workspaces.Models;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Workspaces.Commands;

public sealed record CreateWorkspaceCommand(
    Guid UserId,
    string Name,
    string? Type,
    string? Description) : IRequest<Result<WorkspaceResponse>>;

public sealed class CreateWorkspaceCommandHandler
    : IRequestHandler<CreateWorkspaceCommand, Result<WorkspaceResponse>>
{
    private readonly IRepository<Workspace> _repository;

    public CreateWorkspaceCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Workspace>();
    }

    public async Task<Result<WorkspaceResponse>> Handle(CreateWorkspaceCommand request,
        CancellationToken cancellationToken)
    {
        var workspace = new Workspace
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            Name = request.Name.Trim(),
            Type = string.IsNullOrWhiteSpace(request.Type) ? null : request.Type.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _repository.AddAsync(workspace, cancellationToken);

        return Result.Success(WorkspaceMapping.ToResponse(workspace));
    }
}
