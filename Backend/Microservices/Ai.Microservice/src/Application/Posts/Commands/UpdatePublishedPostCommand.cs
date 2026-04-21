using Domain.Entities;
using Domain.Repositories;
using MassTransit;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Contracts.Publishing;
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record UpdatePublishedPostCommand(
    Guid UserId,
    Guid PostId,
    string NewContent,
    string? NewHashtag = null) : IRequest<Result<UpdatePublishedPostResponse>>;

public sealed record UpdatePublishedPostResponse(
    Guid PostId,
    IReadOnlyList<UpdatePublishedTargetResponse> Targets);

public sealed record UpdatePublishedTargetResponse(
    Guid PublicationId,
    Guid SocialMediaId,
    string SocialMediaType);

public sealed class UpdatePublishedPostCommandHandler
    : IRequestHandler<UpdatePublishedPostCommand, Result<UpdatePublishedPostResponse>>
{
    private const string PublishedStatus = "published";

    private readonly IPostRepository _postRepository;
    private readonly IPostPublicationRepository _postPublicationRepository;
    private readonly IBus _bus;

    public UpdatePublishedPostCommandHandler(
        IPostRepository postRepository,
        IPostPublicationRepository postPublicationRepository,
        IBus bus)
    {
        _postRepository = postRepository;
        _postPublicationRepository = postPublicationRepository;
        _bus = bus;
    }

    public async Task<Result<UpdatePublishedPostResponse>> Handle(
        UpdatePublishedPostCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.NewContent))
        {
            return Result.Failure<UpdatePublishedPostResponse>(
                new Error("Post.EmptyContent", "Updated content cannot be empty."));
        }

        var post = await _postRepository.GetByIdForUpdateAsync(request.PostId, cancellationToken);
        if (post == null || post.DeletedAt.HasValue)
        {
            return Result.Failure<UpdatePublishedPostResponse>(new Error("Post.NotFound", "Post not found."));
        }
        if (post.UserId != request.UserId)
        {
            return Result.Failure<UpdatePublishedPostResponse>(
                new Error("Post.Unauthorized", "You are not authorized to edit this post."));
        }

        var publications = await _postPublicationRepository.GetByPostIdForUpdateAsync(post.Id, cancellationToken);
        var active = publications
            .Where(p => !p.DeletedAt.HasValue &&
                        string.Equals(p.PublishStatus, PublishedStatus, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (active.Count == 0)
        {
            return Result.Failure<UpdatePublishedPostResponse>(
                new Error("Post.NoActivePublications", "This post has no active publications to update."));
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        post.Content = new PostContent
        {
            Content = request.NewContent,
            Hashtag = request.NewHashtag,
            ResourceList = post.Content?.ResourceList ?? new List<string>(),
            PostType = post.Content?.PostType
        };
        post.UpdatedAt = now;
        _postRepository.Update(post);
        await _postRepository.SaveChangesAsync(cancellationToken);

        var correlationId = Guid.CreateVersion7();
        var combined = string.IsNullOrWhiteSpace(request.NewHashtag)
            ? request.NewContent
            : $"{request.NewContent}\n\n{request.NewHashtag}";

        foreach (var publication in active)
        {
            await _bus.Publish(new UpdatePublishedTargetRequested
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
                NewCaption = combined,
                CreatedAt = now
            }, cancellationToken);
        }

        return Result.Success(new UpdatePublishedPostResponse(
            post.Id,
            active.Select(p => new UpdatePublishedTargetResponse(p.Id, p.SocialMediaId, p.SocialMediaType)).ToList()));
    }
}
