using System.Text.Json;
using Application.Abstractions.Data;
using Application.SocialMedias;
using Application.SocialMedias.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.WorkspaceSocialMedias.Commands;

public sealed record UpdateWorkspaceSocialMediaCommand(
    Guid WorkspaceId,
    Guid SocialMediaId,
    Guid UserId,
    string Type,
    JsonDocument? Metadata) : IRequest<Result<SocialMediaResponse>>;

public sealed class UpdateWorkspaceSocialMediaCommandHandler
    : IRequestHandler<UpdateWorkspaceSocialMediaCommand, Result<SocialMediaResponse>>
{
    private readonly IRepository<Workspace> _workspaceRepository;
    private readonly IRepository<SocialMedia> _socialMediaRepository;
    private readonly IRepository<WorkspaceSocialMedia> _linkRepository;

    public UpdateWorkspaceSocialMediaCommandHandler(IUnitOfWork unitOfWork)
    {
        _workspaceRepository = unitOfWork.Repository<Workspace>();
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
        _linkRepository = unitOfWork.Repository<WorkspaceSocialMedia>();
    }

    public async Task<Result<SocialMediaResponse>> Handle(UpdateWorkspaceSocialMediaCommand request,
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

        var link = await _linkRepository.GetAll()
            .FirstOrDefaultAsync(item =>
                    item.WorkspaceId == request.WorkspaceId &&
                    item.SocialMediaId == request.SocialMediaId &&
                    item.UserId == request.UserId &&
                    !item.IsDeleted,
                cancellationToken);

        if (link == null)
        {
            return Result.Failure<SocialMediaResponse>(
                new Error("WorkspaceSocialMedia.NotFound", "Social media not found in workspace"));
        }

        var socialMedia = await _socialMediaRepository.GetAll()
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

        socialMedia.Type = request.Type.Trim();
        socialMedia.Metadata = request.Metadata;
        socialMedia.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _socialMediaRepository.Update(socialMedia);

        link.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _linkRepository.Update(link);

        return Result.Success(SocialMediaMapping.ToResponse(socialMedia));
    }
}
