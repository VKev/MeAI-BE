using Application.Posts.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record UpdatePostCommand(
    Guid PostId,
    Guid UserId,
    Guid? WorkspaceId,
    Guid? SocialMediaId,
    string? Title,
    Domain.Entities.PostContent? Content,
    string? Status) : IRequest<Result<PostResponse>>;

public sealed class UpdatePostCommandHandler
    : IRequestHandler<UpdatePostCommand, Result<PostResponse>>
{
    private readonly IPostRepository _postRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly PostResponseBuilder _postResponseBuilder;

    public UpdatePostCommandHandler(
        IPostRepository postRepository,
        IWorkspaceRepository workspaceRepository,
        PostResponseBuilder postResponseBuilder)
    {
        _postRepository = postRepository;
        _workspaceRepository = workspaceRepository;
        _postResponseBuilder = postResponseBuilder;
    }

    public async Task<Result<PostResponse>> Handle(UpdatePostCommand request, CancellationToken cancellationToken)
    {
        var post = await _postRepository.GetByIdForUpdateAsync(request.PostId, cancellationToken);

        if (post == null || post.DeletedAt.HasValue)
        {
            return Result.Failure<PostResponse>(PostErrors.NotFound);
        }

        if (post.UserId != request.UserId)
        {
            return Result.Failure<PostResponse>(PostErrors.Unauthorized);
        }

        // Treat unprovided fields (null in partial payload) as "don't change" rather than
        // clobbering them — callers sending a partial update (e.g. content-only from the
        // post-builder publish flow) must not wipe workspaceId, status, title, etc.
        var requestedWorkspaceId = NormalizeGuid(request.WorkspaceId);
        if (requestedWorkspaceId.HasValue)
        {
            var workspaceExists = await _workspaceRepository.ExistsForUserAsync(
                requestedWorkspaceId.Value,
                request.UserId,
                cancellationToken);

            if (!workspaceExists)
            {
                return Result.Failure<PostResponse>(PostErrors.WorkspaceNotFound);
            }

            post.WorkspaceId = requestedWorkspaceId;
        }

        var requestedSocialMediaId = NormalizeGuid(request.SocialMediaId);
        if (requestedSocialMediaId.HasValue)
        {
            post.SocialMediaId = requestedSocialMediaId;
        }

        var normalizedTitle = NormalizeString(request.Title);
        if (normalizedTitle is not null)
        {
            post.Title = normalizedTitle;
        }

        if (request.Content is not null)
        {
            post.Content = request.Content;
        }

        var normalizedStatus = NormalizeString(request.Status);
        if (normalizedStatus is not null)
        {
            post.Status = normalizedStatus;
        }

        if (post.ScheduleGroupId.HasValue && post.ScheduledAtUtc.HasValue)
        {
            post.Status = "scheduled";
        }

        post.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        // Consolidate any sibling duplicates that share this post's (PostBuilder, Platform,
        // post_type) bucket. Legacy rows from pre-dedup releases get soft-deleted on the
        // first edit so GetPostBuilder stops surfacing them.
        if (post.PostBuilderId.HasValue)
        {
            var normalizedPlatform = NormalizePlatform(post.Platform);
            var normalizedPostType = NormalizePostType(post.Content?.PostType);
            var siblings = await _postRepository.GetTrackedByPostBuilderIdAsync(
                post.PostBuilderId.Value,
                cancellationToken);

            var now = DateTimeExtensions.PostgreSqlUtcNow;
            foreach (var sibling in siblings)
            {
                if (sibling.Id == post.Id) continue;
                if (sibling.UserId != post.UserId) continue;
                if (NormalizePlatform(sibling.Platform) != normalizedPlatform) continue;
                if (NormalizePostType(sibling.Content?.PostType) != normalizedPostType) continue;

                sibling.DeletedAt = now;
                sibling.UpdatedAt = now;
            }
        }

        _postRepository.Update(post);
        await _postRepository.SaveChangesAsync(cancellationToken);

        var response = await _postResponseBuilder.BuildAsync(request.UserId, post, cancellationToken);
        return Result.Success(response);
    }

    private static string? NormalizeString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static Guid? NormalizeGuid(Guid? value)
    {
        return value == Guid.Empty ? null : value;
    }

    private static string NormalizePlatform(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToLowerInvariant();
        return v switch
        {
            "thread" => "threads",
            "ig" => "instagram",
            "fb" => "facebook",
            _ => v
        };
    }

    private static string NormalizePostType(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (v == "reel" || v == "reels" || v == "video") return "reels";
        return "posts";
    }
}
