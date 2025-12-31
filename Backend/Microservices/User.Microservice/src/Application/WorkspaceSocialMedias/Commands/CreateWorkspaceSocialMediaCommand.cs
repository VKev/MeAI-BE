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

public sealed record CreateWorkspaceSocialMediaCommand(
    Guid WorkspaceId,
    Guid UserId,
    string Type,
    JsonDocument? Metadata) : IRequest<Result<SocialMediaResponse>>;

public sealed class CreateWorkspaceSocialMediaCommandHandler
    : IRequestHandler<CreateWorkspaceSocialMediaCommand, Result<SocialMediaResponse>>
{
    private readonly IRepository<Workspace> _workspaceRepository;
    private readonly IRepository<SocialMedia> _socialMediaRepository;
    private readonly IRepository<WorkspaceSocialMedia> _linkRepository;

    public CreateWorkspaceSocialMediaCommandHandler(IUnitOfWork unitOfWork)
    {
        _workspaceRepository = unitOfWork.Repository<Workspace>();
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
        _linkRepository = unitOfWork.Repository<WorkspaceSocialMedia>();
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

        var socialMedia = new SocialMedia
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            Type = request.Type.Trim(),
            Metadata = request.Metadata,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _socialMediaRepository.AddAsync(socialMedia, cancellationToken);

        var link = new WorkspaceSocialMedia
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            WorkspaceId = request.WorkspaceId,
            SocialMediaId = socialMedia.Id,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _linkRepository.AddAsync(link, cancellationToken);

        return Result.Success(SocialMediaMapping.ToResponse(socialMedia));
    }
}
