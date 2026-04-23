using Application.Posts.Commands;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Logic.Services;

public sealed class ScheduledPostDispatchService
{
    private const int BatchSize = 20;

    private readonly IPostRepository _postRepository;
    private readonly IMediator _mediator;
    private readonly ILogger<ScheduledPostDispatchService> _logger;

    public ScheduledPostDispatchService(
        IPostRepository postRepository,
        IMediator mediator,
        ILogger<ScheduledPostDispatchService> logger)
    {
        _postRepository = postRepository;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<int> DispatchDuePostsAsync(CancellationToken cancellationToken)
    {
        var claimedPosts = await _postRepository.ClaimDueScheduledPostsAsync(
            DateTime.UtcNow,
            BatchSize,
            cancellationToken);

        foreach (var scheduledPost in claimedPosts)
        {
            var result = await _mediator.Send(
                new PublishPostsCommand(
                    scheduledPost.UserId,
                    [new PublishPostTargetInput(
                        scheduledPost.PostId,
                        scheduledPost.SocialMediaIds,
                        scheduledPost.IsPrivate)]),
                cancellationToken);

            if (result.IsFailure)
            {
                await _postRepository.MarkScheduledDispatchFailedAsync(scheduledPost.PostId, cancellationToken);

                _logger.LogWarning(
                    "Scheduled publish dispatch failed. PostId: {PostId}, UserId: {UserId}, Code: {Code}, Description: {Description}",
                    scheduledPost.PostId,
                    scheduledPost.UserId,
                    result.Error.Code,
                    result.Error.Description);
            }
        }

        return claimedPosts.Count;
    }
}
