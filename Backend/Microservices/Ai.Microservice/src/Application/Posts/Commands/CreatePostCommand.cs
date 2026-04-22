using Application.Posts.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record CreatePostCommand(
    Guid UserId,
    Guid? WorkspaceId,
    Guid? SocialMediaId,
    string? Title,
    PostContent? Content,
    string? Status,
    Guid? PostBuilderId = null,
    string? Platform = null) : IRequest<Result<PostResponse>>;

public sealed class CreatePostCommandHandler
    : IRequestHandler<CreatePostCommand, Result<PostResponse>>
{
    private readonly IPostRepository _postRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly PostResponseBuilder _postResponseBuilder;

    public CreatePostCommandHandler(
        IPostRepository postRepository,
        IWorkspaceRepository workspaceRepository,
        PostResponseBuilder postResponseBuilder)
    {
        _postRepository = postRepository;
        _workspaceRepository = workspaceRepository;
        _postResponseBuilder = postResponseBuilder;
    }

    public async Task<Result<PostResponse>> Handle(CreatePostCommand request, CancellationToken cancellationToken)
    {
        var workspaceId = NormalizeGuid(request.WorkspaceId);
        if (workspaceId.HasValue)
        {
            var workspaceExists = await _workspaceRepository.ExistsForUserAsync(
                workspaceId.Value,
                request.UserId,
                cancellationToken);

            if (!workspaceExists)
            {
                return Result.Failure<PostResponse>(PostErrors.WorkspaceNotFound);
            }
        }

        var postBuilderId = NormalizeGuid(request.PostBuilderId);
        var normalizedPlatform = NormalizePlatform(request.Platform);
        var normalizedPostType = NormalizePostType(request.Content?.PostType);

        // Idempotent upsert when tied to a PostBuilder — editing a caption on the FE
        // should edit the existing post in place, never insert a duplicate. Consolidate
        // any existing duplicates in the same (PostBuilder, Platform, post_type) bucket
        // so legacy data from pre-dedup releases gets cleaned up opportunistically.
        if (postBuilderId.HasValue)
        {
            var existing = await _postRepository.GetTrackedByPostBuilderIdAsync(postBuilderId.Value, cancellationToken);
            var siblings = existing
                .Where(p => p.UserId == request.UserId
                            && NormalizePlatform(p.Platform) == normalizedPlatform
                            && NormalizePostType(p.Content?.PostType) == normalizedPostType)
                .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt ?? DateTime.MinValue)
                .ThenByDescending(p => p.Id)
                .ToList();

            if (siblings.Count > 0)
            {
                var primary = siblings[0];
                primary.Title = NormalizeString(request.Title);
                primary.Content = request.Content;
                primary.Status = NormalizeString(request.Status);
                primary.SocialMediaId = NormalizeGuid(request.SocialMediaId);
                primary.WorkspaceId = workspaceId;
                primary.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

                // Soft-delete duplicates so GetPostBuilder no longer surfaces them. Hard
                // delete is unsafe here — Publications rows may still reference them.
                var now = DateTimeExtensions.PostgreSqlUtcNow;
                for (var i = 1; i < siblings.Count; i++)
                {
                    siblings[i].DeletedAt = now;
                    siblings[i].UpdatedAt = now;
                }

                await _postRepository.SaveChangesAsync(cancellationToken);
                var existingResponse = await _postResponseBuilder.BuildAsync(request.UserId, primary, cancellationToken);
                return Result.Success(existingResponse);
            }
        }

        var post = new Post
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            WorkspaceId = workspaceId,
            SocialMediaId = NormalizeGuid(request.SocialMediaId),
            PostBuilderId = postBuilderId,
            Platform = NormalizeString(request.Platform),
            Title = NormalizeString(request.Title),
            Content = request.Content,
            Status = NormalizeString(request.Status),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow,
            UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _postRepository.AddAsync(post, cancellationToken);
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

    // FE uses `thread` (no s); DB has both `thread` and `threads`. Instagram also has
    // `ig` as a legacy alias. Canonicalize so dedup matches across historical values.
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

    // Mirror the FE's normalizePostType: "reel"/"reels"/"video" → "reels", everything
    // else (incl. "post"/"posts"/null/empty) → "posts". Prevents createPost from
    // inserting a sibling row with a slightly different post_type string.
    private static string NormalizePostType(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (v == "reel" || v == "reels" || v == "video") return "reels";
        return "posts";
    }
}
