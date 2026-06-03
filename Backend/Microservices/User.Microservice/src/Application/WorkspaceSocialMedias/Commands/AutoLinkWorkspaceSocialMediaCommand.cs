using Application.Abstractions.Data;
using Application.Abstractions.SocialMedia;
using Application.SocialMedias;
using Application.SocialMedias.Commands;
using Application.SocialMedias.Models;
using Domain.Entities;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.WorkspaceSocialMedias.Commands;

public sealed record AutoLinkWorkspaceSocialMediaCommand(
    Guid WorkspaceId,
    Guid UserId,
    Guid? SocialMediaId,
    string? Platform) : IRequest<Result<List<SocialMediaResponse>>>;

public sealed class AutoLinkWorkspaceSocialMediaCommandHandler
    : IRequestHandler<AutoLinkWorkspaceSocialMediaCommand, Result<List<SocialMediaResponse>>>
{
    private static readonly IReadOnlyDictionary<string, string[]> PlatformAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["facebook"] = ["facebook"],
            ["instagram"] = ["instagram", "ig"],
            ["ig"] = ["instagram", "ig"],
            ["tiktok"] = ["tiktok"],
            ["threads"] = ["threads", "thread"],
            ["thread"] = ["threads", "thread"]
        };

    private readonly IRepository<Workspace> _workspaceRepository;
    private readonly IRepository<SocialMedia> _socialMediaRepository;
    private readonly IRepository<WorkspaceSocialMedia> _linkRepository;
    private readonly ISocialMediaProfileService _profileService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<AutoLinkWorkspaceSocialMediaCommandHandler> _logger;

    public AutoLinkWorkspaceSocialMediaCommandHandler(
        IUnitOfWork unitOfWork,
        ISocialMediaProfileService profileService,
        IPublishEndpoint publishEndpoint,
        ILogger<AutoLinkWorkspaceSocialMediaCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _workspaceRepository = unitOfWork.Repository<Workspace>();
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
        _linkRepository = unitOfWork.Repository<WorkspaceSocialMedia>();
        _profileService = profileService;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<Result<List<SocialMediaResponse>>> Handle(
        AutoLinkWorkspaceSocialMediaCommand request,
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
            return Result.Failure<List<SocialMediaResponse>>(
                new Error("Workspace.NotFound", "Workspace not found"));
        }

        var platformAliases = ResolvePlatformAliases(request.Platform);
        if (request.SocialMediaId == null && platformAliases.Count == 0)
        {
            return Result.Failure<List<SocialMediaResponse>>(
                new Error("WorkspaceSocialMedia.PlatformRequired", "Platform or social media id is required"));
        }

        var socialMediaQuery = _socialMediaRepository.GetAll()
            .AsNoTracking()
            .Where(item => item.UserId == request.UserId && !item.IsDeleted);

        if (platformAliases.Count > 0)
        {
            socialMediaQuery = socialMediaQuery.Where(item => platformAliases.Contains(item.Type));
        }
        else if (request.SocialMediaId.HasValue)
        {
            socialMediaQuery = socialMediaQuery.Where(item => item.Id == request.SocialMediaId.Value);
        }

        var socialMedias = await socialMediaQuery
            .OrderByDescending(item => item.UpdatedAt ?? item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (request.SocialMediaId.HasValue && platformAliases.Count > 0)
        {
            socialMedias = socialMedias
                .OrderByDescending(item => item.Id == request.SocialMediaId.Value)
                .ThenByDescending(item => item.UpdatedAt ?? item.CreatedAt)
                .ThenByDescending(item => item.Id)
                .ToList();
        }

        if (socialMedias.Count == 0)
        {
            return Result.Failure<List<SocialMediaResponse>>(
                new Error("SocialMedia.NotFound", "No matching social media account found"));
        }

        var socialMediaIds = socialMedias.Select(item => item.Id).ToList();
        var existingLinks = await _linkRepository.GetAll()
            .Where(item =>
                item.WorkspaceId == request.WorkspaceId &&
                item.UserId == request.UserId &&
                socialMediaIds.Contains(item.SocialMediaId))
            .ToListAsync(cancellationToken);

        var linksBySocialMediaId = existingLinks
            .GroupBy(item => item.SocialMediaId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.CreatedAt ?? DateTime.MinValue)
                    .ThenByDescending(item => item.Id)
                    .First());

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        foreach (var socialMedia in socialMedias)
        {
            if (linksBySocialMediaId.TryGetValue(socialMedia.Id, out var existingLink))
            {
                if (existingLink.IsDeleted)
                {
                    existingLink.IsDeleted = false;
                    existingLink.DeletedAt = null;
                    existingLink.UpdatedAt = now;
                    _linkRepository.Update(existingLink);
                }

                continue;
            }

            var link = new WorkspaceSocialMedia
            {
                Id = Guid.CreateVersion7(),
                UserId = request.UserId,
                WorkspaceId = request.WorkspaceId,
                SocialMediaId = socialMedia.Id,
                CreatedAt = now
            };

            await _linkRepository.AddAsync(link, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await SocialMediaPostSyncEventPublisher.PublishAsync(
            _publishEndpoint,
            _logger,
            request.UserId,
            socialMedias,
            cancellationToken,
            request.WorkspaceId,
            trigger: "workspace_auto_link");

        var responses = await Task.WhenAll(
            socialMedias.Select(async socialMedia =>
            {
                var profileResult = await _profileService.GetUserProfileAsync(
                    socialMedia.Type,
                    socialMedia.Metadata,
                    cancellationToken);

                var profile = profileResult.IsSuccess ? profileResult.Value : null;
                return SocialMediaMapping.ToResponse(socialMedia, profile);
            }));

        return Result.Success(responses.ToList());
    }

    private static IReadOnlyList<string> ResolvePlatformAliases(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return [];
        }

        var normalized = platform.Trim().ToLowerInvariant();
        return PlatformAliases.TryGetValue(normalized, out var aliases)
            ? aliases
            : [normalized];
    }
}
