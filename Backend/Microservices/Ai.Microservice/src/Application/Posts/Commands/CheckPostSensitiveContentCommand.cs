using Application.Abstractions.Gemini;
using Application.Posts;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Commands;

public sealed record CheckPostSensitiveContentCommand(
    Guid PostId,
    Guid UserId) : IRequest<Result<CheckSensitiveContentResponse>>;

public sealed record CheckSensitiveContentResponse(
    Guid PostId,
    bool IsSensitive,
    string? Category,
    string? Reason,
    double ConfidenceScore);

public sealed class CheckPostSensitiveContentCommandHandler
    : IRequestHandler<CheckPostSensitiveContentCommand, Result<CheckSensitiveContentResponse>>
{
    private readonly IPostRepository _postRepository;
    private readonly IGeminiContentModerationService _contentModerationService;

    public CheckPostSensitiveContentCommandHandler(
        IPostRepository postRepository,
        IGeminiContentModerationService contentModerationService)
    {
        _postRepository = postRepository;
        _contentModerationService = contentModerationService;
    }

    public async Task<Result<CheckSensitiveContentResponse>> Handle(
        CheckPostSensitiveContentCommand request,
        CancellationToken cancellationToken)
    {
        var post = await _postRepository.GetByIdAsync(request.PostId, cancellationToken);

        if (post == null || post.DeletedAt.HasValue)
        {
            return Result.Failure<CheckSensitiveContentResponse>(PostErrors.NotFound);
        }

        if (post.UserId != request.UserId)
        {
            return Result.Failure<CheckSensitiveContentResponse>(PostErrors.Unauthorized);
        }

        var text = post.Content?.Content?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return Result.Success(new CheckSensitiveContentResponse(
                post.Id,
                IsSensitive: false,
                Category: null,
                Reason: "Post has no text content to analyze.",
                ConfidenceScore: 0.0));
        }

        var moderationResult = await _contentModerationService.CheckSensitiveContentAsync(
            new ContentModerationRequest(text),
            cancellationToken);

        if (moderationResult.IsFailure)
        {
            return Result.Failure<CheckSensitiveContentResponse>(moderationResult.Error);
        }

        var result = moderationResult.Value;
        return Result.Success(new CheckSensitiveContentResponse(
            post.Id,
            result.IsSensitive,
            result.Category,
            result.Reason,
            result.ConfidenceScore));
    }
}
