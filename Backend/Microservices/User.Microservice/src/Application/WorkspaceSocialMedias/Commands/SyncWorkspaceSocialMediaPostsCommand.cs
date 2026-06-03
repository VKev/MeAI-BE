using Application.Abstractions.Data;
using Application.SocialMedias.Commands;
using Domain.Entities;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.WorkspaceSocialMedias.Commands;

public sealed record SyncWorkspaceSocialMediaPostsCommand(
    Guid WorkspaceId,
    Guid UserId) : IRequest<Result<int>>;

public sealed class SyncWorkspaceSocialMediaPostsCommandHandler
    : IRequestHandler<SyncWorkspaceSocialMediaPostsCommand, Result<int>>
{
    private readonly IRepository<Workspace> _workspaceRepository;
    private readonly IRepository<WorkspaceSocialMedia> _linkRepository;
    private readonly IRepository<SocialMedia> _socialMediaRepository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<SyncWorkspaceSocialMediaPostsCommandHandler> _logger;

    public SyncWorkspaceSocialMediaPostsCommandHandler(
        IUnitOfWork unitOfWork,
        IPublishEndpoint publishEndpoint,
        ILogger<SyncWorkspaceSocialMediaPostsCommandHandler> logger)
    {
        _workspaceRepository = unitOfWork.Repository<Workspace>();
        _linkRepository = unitOfWork.Repository<WorkspaceSocialMedia>();
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<Result<int>> Handle(
        SyncWorkspaceSocialMediaPostsCommand request,
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
            return Result.Failure<int>(new Error("Workspace.NotFound", "Workspace not found"));
        }

        var socialMediaIds = await _linkRepository.GetAll()
            .AsNoTracking()
            .Where(item =>
                item.WorkspaceId == request.WorkspaceId &&
                item.UserId == request.UserId &&
                !item.IsDeleted)
            .Select(item => item.SocialMediaId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (socialMediaIds.Count == 0)
        {
            return Result.Success(0);
        }

        var socialMedias = await _socialMediaRepository.GetAll()
            .AsNoTracking()
            .Where(item =>
                item.UserId == request.UserId &&
                socialMediaIds.Contains(item.Id) &&
                !item.IsDeleted)
            .ToListAsync(cancellationToken);

        await SocialMediaPostSyncEventPublisher.PublishAsync(
            _publishEndpoint,
            _logger,
            request.UserId,
            socialMedias,
            cancellationToken,
            request.WorkspaceId,
            trigger: "workspace_manual_sync");

        return Result.Success(socialMedias.Count);
    }
}
