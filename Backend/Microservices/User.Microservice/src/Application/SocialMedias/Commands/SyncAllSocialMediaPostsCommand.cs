using Application.Abstractions.Data;
using Domain.Entities;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.SocialMedias.Commands;

public sealed record SyncAllSocialMediaPostsCommand(Guid UserId) : IRequest<Result<int>>;

public sealed class SyncAllSocialMediaPostsCommandHandler
    : IRequestHandler<SyncAllSocialMediaPostsCommand, Result<int>>
{
    private readonly IRepository<SocialMedia> _socialMediaRepository;
    private readonly IRepository<Workspace> _workspaceRepository;
    private readonly IRepository<WorkspaceSocialMedia> _workspaceSocialMediaRepository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<SyncAllSocialMediaPostsCommandHandler> _logger;

    public SyncAllSocialMediaPostsCommandHandler(
        IUnitOfWork unitOfWork,
        IPublishEndpoint publishEndpoint,
        ILogger<SyncAllSocialMediaPostsCommandHandler> logger)
    {
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
        _workspaceRepository = unitOfWork.Repository<Workspace>();
        _workspaceSocialMediaRepository = unitOfWork.Repository<WorkspaceSocialMedia>();
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<Result<int>> Handle(
        SyncAllSocialMediaPostsCommand request,
        CancellationToken cancellationToken)
    {
        var socialMedias = await _socialMediaRepository.GetAll()
            .AsNoTracking()
            .Where(item =>
                item.UserId == request.UserId &&
                !item.IsDeleted)
            .OrderBy(item => item.Type)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);

        if (socialMedias.Count == 0)
        {
            return Result.Success(0);
        }

        var activeWorkspaceIds = _workspaceRepository.GetAll()
            .AsNoTracking()
            .Where(workspace =>
                workspace.UserId == request.UserId &&
                !workspace.IsDeleted)
            .Select(workspace => workspace.Id);

        var workspaceLinks = await _workspaceSocialMediaRepository.GetAll()
            .AsNoTracking()
            .Where(link =>
                link.UserId == request.UserId &&
                !link.IsDeleted &&
                activeWorkspaceIds.Contains(link.WorkspaceId))
            .Select(link => new { link.SocialMediaId, link.WorkspaceId })
            .Distinct()
            .ToListAsync(cancellationToken);

        var workspaceIdsBySocialMediaId = workspaceLinks
            .GroupBy(link => link.SocialMediaId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(link => link.WorkspaceId).Distinct().ToList());
        var requestedAt = DateTimeExtensions.PostgreSqlUtcNow;

        foreach (var socialMedia in socialMedias)
        {
            if (!workspaceIdsBySocialMediaId.TryGetValue(socialMedia.Id, out var workspaceIds) ||
                workspaceIds.Count == 0)
            {
                await QueueSyncAsync(socialMedia, null, requestedAt, cancellationToken);
                continue;
            }

            foreach (var workspaceId in workspaceIds)
            {
                await QueueSyncAsync(socialMedia, workspaceId, requestedAt, cancellationToken);
            }
        }

        return Result.Success(socialMedias.Count);
    }

    private Task QueueSyncAsync(
        SocialMedia socialMedia,
        Guid? workspaceId,
        DateTime requestedAt,
        CancellationToken cancellationToken)
    {
        return SocialMediaPostSyncEventPublisher.PublishAsync(
            _publishEndpoint,
            _logger,
            socialMedia.UserId,
            [socialMedia],
            cancellationToken,
            workspaceId,
            trigger: "manual_sync_all_accounts",
            requestedAt: requestedAt);
    }
}
