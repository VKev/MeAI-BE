using Application.Posts.Models;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record CreatePostCommand(
    Guid UserId,
    Guid? SocialMediaId,
    string? Title,
    PostContent? Content,
    string? Status) : IRequest<Result<PostResponse>>;

public sealed class CreatePostCommandHandler
    : IRequestHandler<CreatePostCommand, Result<PostResponse>>
{
    private readonly IPostRepository _postRepository;

    public CreatePostCommandHandler(IPostRepository postRepository)
    {
        _postRepository = postRepository;
    }

    public async Task<Result<PostResponse>> Handle(CreatePostCommand request, CancellationToken cancellationToken)
    {
        var post = new Post
        {
            Id = Guid.CreateVersion7(),
            UserId = request.UserId,
            SocialMediaId = NormalizeGuid(request.SocialMediaId),
            Title = NormalizeString(request.Title),
            Content = request.Content,
            Status = NormalizeString(request.Status),
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _postRepository.AddAsync(post, cancellationToken);
        await _postRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(PostMapping.ToResponse(post));
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
