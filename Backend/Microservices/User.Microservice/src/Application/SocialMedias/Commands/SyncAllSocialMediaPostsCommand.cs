using Application.Abstractions.Data;
using Domain.Entities;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.SocialMedias.Commands;

public sealed record SyncAllSocialMediaPostsCommand(Guid UserId) : IRequest<Result<int>>;

public sealed class SyncAllSocialMediaPostsCommandHandler
    : IRequestHandler<SyncAllSocialMediaPostsCommand, Result<int>>
{
    private readonly IRepository<SocialMedia> _socialMediaRepository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<SyncAllSocialMediaPostsCommandHandler> _logger;

    public SyncAllSocialMediaPostsCommandHandler(
        IUnitOfWork unitOfWork,
        IPublishEndpoint publishEndpoint,
        ILogger<SyncAllSocialMediaPostsCommandHandler> logger)
    {
        _socialMediaRepository = unitOfWork.Repository<SocialMedia>();
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

        await SocialMediaPostSyncEventPublisher.PublishAsync(
            _publishEndpoint,
            _logger,
            request.UserId,
            socialMedias,
            cancellationToken,
            workspaceId: null,
            trigger: "manual_sync_all_accounts");

        return Result.Success(socialMedias.Count);
    }
}
