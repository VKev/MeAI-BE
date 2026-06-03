using Application.Abstractions.Data;
using Application.SocialMedias.Commands;
using Domain.Entities;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    private readonly IRepository<SocialMedia> _socialMediaRepository;
    private readonly IRepository<WorkspaceSocialMedia> _linkRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<DeleteWorkspaceSocialMediaCommandHandler> _logger;

    public DeleteWorkspaceSocialMediaCommandHandler(
        IUnitOfWork unitOfWork,
        IPublishEndpoint publishEndpoint,
        ILogger<DeleteWorkspaceSocialMediaCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _workspaceRepository = unitOfWork.Repository<Workspace>();
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
        _linkRepository = unitOfWork.Repository<WorkspaceSocialMedia>();
        _publishEndpoint = publishEndpoint;
        _logger = logger;
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
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var socialMedia = await _socialMediaRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(item =>
                    item.Id == request.SocialMediaId &&
                    item.UserId == request.UserId &&
                    !item.IsDeleted,
                cancellationToken);

        if (socialMedia is not null)
        {
            await SocialMediaPostSyncEventPublisher.PublishAsync(
                _publishEndpoint,
                _logger,
                request.UserId,
                [socialMedia],
                cancellationToken,
                request.WorkspaceId,
                trigger: "workspace_unlink",
                removeFromWorkspace: true);
        }

        return Result.Success(true);
    }
}
