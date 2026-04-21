using Application.Posts.Models;
using Domain.Repositories;
using MassTransit;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Publishing;
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record UnpublishPostCommand(
    Guid UserId,
    Guid PostId) : IRequest<Result<UnpublishPostResponse>>;

public sealed record UnpublishPostResponse(
    Guid PostId,
    string Status,
    IReadOnlyList<UnpublishTargetResponse> Targets);

public sealed record UnpublishTargetResponse(
    Guid PublicationId,
    Guid SocialMediaId,
    string SocialMediaType,
    string ExternalContentId);

public sealed class UnpublishPostCommandHandler
    : IRequestHandler<UnpublishPostCommand, Result<UnpublishPostResponse>>
{
    private const string UnpublishingStatus = "unpublishing";
    private const string PublishedStatus = "published";

    private readonly IPostRepository _postRepository;
    private readonly IPostPublicationRepository _postPublicationRepository;
    private readonly IBus _bus;

    public UnpublishPostCommandHandler(
        IPostRepository postRepository,
        IPostPublicationRepository postPublicationRepository,
        IBus bus)
    {
        _postRepository = postRepository;
        _postPublicationRepository = postPublicationRepository;
        _bus = bus;
    }

    public async Task<Result<UnpublishPostResponse>> Handle(
        UnpublishPostCommand request,
        CancellationToken cancellationToken)
    {
        var post = await _postRepository.GetByIdForUpdateAsync(request.PostId, cancellationToken);
        if (post == null || post.DeletedAt.HasValue)
        {
            return Result.Failure<UnpublishPostResponse>(new Error("Post.NotFound", "Post not found."));
        }

        if (post.UserId != request.UserId)
        {
            return Result.Failure<UnpublishPostResponse>(
                new Error("Post.Unauthorized", "You are not authorized to unpublish this post."));
        }

        if (!post.WorkspaceId.HasValue)
        {
            return Result.Failure<UnpublishPostResponse>(PostErrors.WorkspaceIdRequired);
        }

        var publications = await _postPublicationRepository.GetByPostIdForUpdateAsync(post.Id, cancellationToken);
        var active = publications
            .Where(p => !p.DeletedAt.HasValue &&
                        string.Equals(p.PublishStatus, PublishedStatus, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (active.Count == 0)
        {
            return Result.Failure<UnpublishPostResponse>(
                new Error("Post.NoActivePublications", "This post has no active publications to unpublish."));
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var correlationId = Guid.CreateVersion7();

        foreach (var publication in active)
        {
            publication.PublishStatus = UnpublishingStatus;
            publication.UpdatedAt = now;
            _postPublicationRepository.Update(publication);
        }

        post.Status = UnpublishingStatus;
        post.UpdatedAt = now;
        _postRepository.Update(post);

        await _postRepository.SaveChangesAsync(cancellationToken);

        var messages = active.Select(publication => new UnpublishFromTargetRequested
        {
            CorrelationId = correlationId,
            UserId = request.UserId,
            WorkspaceId = post.WorkspaceId!.Value,
            PostId = post.Id,
            PublicationId = publication.Id,
            SocialMediaId = publication.SocialMediaId,
            SocialMediaType = publication.SocialMediaType,
            ExternalContentId = publication.ExternalContentId,
            DestinationOwnerId = publication.DestinationOwnerId,
            CreatedAt = now
        }).ToList();

        foreach (var message in messages)
        {
            await _bus.Publish(message, cancellationToken);
        }

        var response = new UnpublishPostResponse(
            post.Id,
            UnpublishingStatus,
            active.Select(p => new UnpublishTargetResponse(
                p.Id,
                p.SocialMediaId,
                p.SocialMediaType,
                p.ExternalContentId)).ToList());

        return Result.Success(response);
    }
}
