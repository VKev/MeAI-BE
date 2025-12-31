using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.WorkspaceSocialMedias.Commands;

public sealed record DeleteWorkspaceSocialMediaCommand(
    Guid WorkspaceId,
    Guid SocialMediaId,
    Guid UserId) : IRequest<Result<bool>>;

public sealed class DeleteWorkspaceSocialMediaCommandHandler
    : IRequestHandler<DeleteWorkspaceSocialMediaCommand, Result<bool>>
{
    private readonly IRepository<Workspace> _workspaceRepository;
    private readonly IRepository<WorkspaceSocialMedia> _linkRepository;

    public DeleteWorkspaceSocialMediaCommandHandler(IUnitOfWork unitOfWork)
    {
        _workspaceRepository = unitOfWork.Repository<Workspace>();
        _linkRepository = unitOfWork.Repository<WorkspaceSocialMedia>();
    }

    public async Task<Result<bool>> Handle(DeleteWorkspaceSocialMediaCommand request,
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
            return Result.Failure<bool>(new Error("Workspace.NotFound", "Workspace not found"));
        }

        var link = await _linkRepository.GetAll()
            .FirstOrDefaultAsync(item =>
                    item.WorkspaceId == request.WorkspaceId &&
                    item.SocialMediaId == request.SocialMediaId &&
                    item.UserId == request.UserId &&
                    !item.IsDeleted,
                cancellationToken);

        if (link == null)
        {
            return Result.Failure<bool>(
                new Error("WorkspaceSocialMedia.NotFound", "Social media not found in workspace"));
        }

        link.DeletedAt = DateTimeExtensions.PostgreSqlUtcNow;
        link.IsDeleted = true;
        link.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _linkRepository.Update(link);

        return Result.Success(true);
    }
}
