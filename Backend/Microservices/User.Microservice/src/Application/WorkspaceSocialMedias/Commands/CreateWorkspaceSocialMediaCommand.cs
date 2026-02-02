using Application.Abstractions.Data;
using Application.Abstractions.SocialMedia;
using Application.SocialMedias;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.WorkspaceSocialMedias.Commands;

public sealed record CreateWorkspaceSocialMediaCommand(
    Guid WorkspaceId,
    Guid UserId,
    Guid SocialMediaId) : IRequest<Result<SocialMediaResponse>>;

public sealed class CreateWorkspaceSocialMediaCommandHandler
    : IRequestHandler<CreateWorkspaceSocialMediaCommand, Result<SocialMediaResponse>>
{
    private readonly IRepository<Workspace> _workspaceRepository;
    private readonly IRepository<SocialMedia> _socialMediaRepository;
    private readonly IRepository<WorkspaceSocialMedia> _linkRepository;
    private readonly ISocialMediaProfileService _profileService;

    public CreateWorkspaceSocialMediaCommandHandler(IUnitOfWork unitOfWork, ISocialMediaProfileService profileService)
    {
        _workspaceRepository = unitOfWork.Repository<Workspace>();
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
        _linkRepository = unitOfWork.Repository<WorkspaceSocialMedia>();
        _profileService = profileService;
    }

    public async Task<Result<SocialMediaResponse>> Handle(CreateWorkspaceSocialMediaCommand request,
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
            return Result.Failure<SocialMediaResponse>(
                new Error("Workspace.NotFound", "Workspace not found"));
        }

        var socialMedia = await _socialMediaRepository.GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(item =>
                    item.Id == request.SocialMediaId &&
                    item.UserId == request.UserId &&
                    !item.IsDeleted,
                cancellationToken);

        if (socialMedia == null)
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("SocialMedia.NotFound", "Social media not found"));
        }

        var existingLink = await _linkRepository.GetAll()
            .AsNoTracking()
            .AnyAsync(item =>
                    item.WorkspaceId == request.WorkspaceId &&
                    item.SocialMediaId == request.SocialMediaId &&
                    item.UserId == request.UserId &&
                    !item.IsDeleted,
                cancellationToken);

        if (existingLink)
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("WorkspaceSocialMedia.AlreadyExists", "Social media is already linked to this workspace"));
        }

        var link = new WorkspaceSocialMedia
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            WorkspaceId = request.WorkspaceId,
            SocialMediaId = socialMedia.Id,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _linkRepository.AddAsync(link, cancellationToken);

        var profileResult = await _profileService.GetUserProfileAsync(
            socialMedia.Type,
            socialMedia.Metadata,
            cancellationToken);

        var profile = profileResult.IsSuccess ? profileResult.Value : null;
        return Result.Success(SocialMediaMapping.ToResponse(socialMedia, profile));
    }
}
