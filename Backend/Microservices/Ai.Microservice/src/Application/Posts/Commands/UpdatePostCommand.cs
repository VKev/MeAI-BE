using Application.Posts.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Posts.Commands;

public sealed record UpdatePostCommand(
    Guid PostId,
    Guid UserId,
    Guid? SocialMediaId,
    string? Title,
    Domain.Entities.PostContent? Content,
    string? Status) : IRequest<Result<PostResponse>>;

public sealed class UpdatePostCommandHandler
    : IRequestHandler<UpdatePostCommand, Result<PostResponse>>
{
    private readonly IPostRepository _postRepository;

    public UpdatePostCommandHandler(IPostRepository postRepository)
    {
        _postRepository = postRepository;
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

        post.SocialMediaId = NormalizeGuid(request.SocialMediaId);
        post.Title = NormalizeString(request.Title);
        post.Content = request.Content;
        post.Status = NormalizeString(request.Status);
        post.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        _postRepository.Update(post);
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
