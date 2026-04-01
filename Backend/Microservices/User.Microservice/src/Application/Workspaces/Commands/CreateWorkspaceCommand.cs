using Application.Abstractions.Data;
using Application.Subscriptions.Services;
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
    private readonly IUserSubscriptionEntitlementService _userSubscriptionEntitlementService;

    public CreateWorkspaceCommandHandler(
        IUnitOfWork unitOfWork,
        IUserSubscriptionEntitlementService userSubscriptionEntitlementService)
    {
        _repository = unitOfWork.Repository<Workspace>();
        _userSubscriptionEntitlementService = userSubscriptionEntitlementService;
    }

    public async Task<Result<WorkspaceResponse>> Handle(CreateWorkspaceCommand request,
        CancellationToken cancellationToken)
    {
        var entitlementResult = await _userSubscriptionEntitlementService.EnsureWorkspaceCreationAllowedAsync(
            request.UserId,
            cancellationToken);

        if (entitlementResult.IsFailure)
        {
            return Result.Failure<WorkspaceResponse>(entitlementResult.Error);
        }

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
