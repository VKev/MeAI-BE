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
    string? Status) : IRequest<Result<PostResponse>>;

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

        var post = new Post
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            WorkspaceId = workspaceId,
            SocialMediaId = NormalizeGuid(request.SocialMediaId),
            Title = NormalizeString(request.Title),
            Content = request.Content,
            Status = NormalizeString(request.Status),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
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
}
