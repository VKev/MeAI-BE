using Application.Abstractions.Data;
using Application.SocialMedias;
using Domain.Entities;
using Infrastructure.Configs;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedLibrary.Common;
using SharedLibrary.Contracts.SocialMedia;
using SharedLibrary.Extensions;
using SocialMediaEntity = Domain.Entities.SocialMedia;

namespace Infrastructure.Logic.SocialMedia;

public sealed class SocialMediaPostSyncDispatcher
{
    private const string ScheduledTrigger = "scheduled_cron";

    private readonly IRepository<SocialMediaEntity> _socialMediaRepository;
    private readonly IRepository<Workspace> _workspaceRepository;
    private readonly IRepository<WorkspaceSocialMedia> _workspaceSocialMediaRepository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IOptionsMonitor<SocialMediaPostSyncOptions> _options;
    private readonly ILogger<SocialMediaPostSyncDispatcher> _logger;

    public SocialMediaPostSyncDispatcher(
        IUnitOfWork unitOfWork,
        IPublishEndpoint publishEndpoint,
        IOptionsMonitor<SocialMediaPostSyncOptions> options,
        ILogger<SocialMediaPostSyncDispatcher> logger)
    {
        _socialMediaRepository = unitOfWork.Repository<SocialMediaEntity>();
        _workspaceRepository = unitOfWork.Repository<Workspace>();
        _workspaceSocialMediaRepository = unitOfWork.Repository<WorkspaceSocialMedia>();
        _publishEndpoint = publishEndpoint;
        _options = options;
        _logger = logger;
    }

    public async Task<int> QueueRecurringSyncsAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled)
        {
            return 0;
        }

        var targets = await GetTargetsAsync(options, cancellationToken);
        if (targets.Count == 0)
        {
            return 0;
        }

        var requestedAt = DateTimeExtensions.PostgreSqlUtcNow;
        var pageLimit = Clamp(options.PageLimit, 1, 100, 50);
        var maxPages = Clamp(options.MaxPages, 1, 500, 2);
        var queued = 0;

        foreach (var target in targets)
        {
            await _publishEndpoint.Publish(
                new SyncSocialMediaPostsRequested
                {
                    CorrelationId = Guid.CreateVersion7(),
                    UserId = target.UserId,
                    SocialMediaId = target.SocialMediaId,
                    WorkspaceId = target.WorkspaceId,
                    Platform = target.Platform,
                    ExternalAccountKey = target.ExternalAccountKey,
                    Trigger = ScheduledTrigger,
                    PageLimit = pageLimit,
                    MaxPages = maxPages,
                    RequestedAt = requestedAt,
                    SuppressSuccessNotification = options.SuppressSuccessNotifications,
                    SuppressFailureNotification = options.SuppressFailureNotifications
                },
                cancellationToken);

            queued++;
        }

        _logger.LogInformation(
            "Queued recurring social media post sync targets. TargetCount={TargetCount}, PageLimit={PageLimit}, MaxPages={MaxPages}",
            queued,
            pageLimit,
            maxPages);

        return queued;
    }

    private async Task<IReadOnlyList<SocialMediaPostSyncTarget>> GetTargetsAsync(
        SocialMediaPostSyncOptions options,
        CancellationToken cancellationToken)
    {
        var socialMedias = _socialMediaRepository.GetAll()
            .AsNoTracking()
            .Where(item => !item.IsDeleted);

        var workspaceLinks = _workspaceSocialMediaRepository.GetAll()
            .AsNoTracking()
            .Where(item => !item.IsDeleted);

        var workspaces = _workspaceRepository.GetAll()
            .AsNoTracking()
            .Where(item => !item.IsDeleted);

        var activeWorkspaceLinks =
            from link in workspaceLinks
            join workspace in workspaces
                on new { link.WorkspaceId, link.UserId }
                equals new { WorkspaceId = workspace.Id, workspace.UserId }
            select link;

        var linkedRows = await (
            from socialMedia in socialMedias
            join link in activeWorkspaceLinks
                on new { SocialMediaId = socialMedia.Id, socialMedia.UserId }
                equals new { link.SocialMediaId, link.UserId }
            orderby socialMedia.UserId, socialMedia.Id, link.WorkspaceId
            select new { SocialMedia = socialMedia, link.WorkspaceId })
            .ToListAsync(cancellationToken);

        var unlinkedRows = await socialMedias
            .Where(socialMedia => !activeWorkspaceLinks.Any(link =>
                link.UserId == socialMedia.UserId &&
                link.SocialMediaId == socialMedia.Id))
            .OrderBy(socialMedia => socialMedia.UserId)
            .ThenBy(socialMedia => socialMedia.Id)
            .ToListAsync(cancellationToken);

        var linkedTargets = linkedRows.Select(row => new SocialMediaPostSyncTarget(
            row.SocialMedia.UserId,
            row.SocialMedia.Id,
            row.WorkspaceId,
            row.SocialMedia.Type,
            SocialMediaExternalAccountKey.Resolve(row.SocialMedia)));

        var unlinkedTargets = unlinkedRows.Select(socialMedia => new SocialMediaPostSyncTarget(
            socialMedia.UserId,
            socialMedia.Id,
            null,
            socialMedia.Type,
            SocialMediaExternalAccountKey.Resolve(socialMedia)));

        var maxTargets = Clamp(options.MaxTargetsPerRun, 1, 10_000, 500);
        return linkedTargets
            .Concat(unlinkedTargets)
            .GroupBy(target => new { target.UserId, target.SocialMediaId, target.WorkspaceId })
            .Select(group => group.First())
            .Take(maxTargets)
            .ToList();
    }

    private static int Clamp(int value, int min, int max, int fallback)
    {
        if (value <= 0)
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }

    private sealed record SocialMediaPostSyncTarget(
        Guid UserId,
        Guid SocialMediaId,
        Guid? WorkspaceId,
        string Platform,
        string ExternalAccountKey);
}
