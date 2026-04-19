using Application.Abstractions.Data;
using Application.Abstractions.Resources;
using Application.Common;
using Application.Posts.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Queries;

public sealed record GetPostsByUsernameQuery(
    string Username,
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int? Limit,
    Guid? RequestingUserId) : IQuery<IReadOnlyList<PostResponse>>;

public sealed class GetPostsByUsernameQueryHandler : IQueryHandler<GetPostsByUsernameQuery, IReadOnlyList<PostResponse>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserResourceService _userResourceService;

    public GetPostsByUsernameQueryHandler(IUnitOfWork unitOfWork, IUserResourceService userResourceService)
    {
        _unitOfWork = unitOfWork;
        _userResourceService = userResourceService;
    }

    public async Task<Result<IReadOnlyList<PostResponse>>> Handle(GetPostsByUsernameQuery request, CancellationToken cancellationToken)
    {
        var username = FeedModerationSupport.NormalizeUsername(request.Username);
        if (username is null)
        {
            return Result.Failure<IReadOnlyList<PostResponse>>(FeedErrors.InvalidUsername);
        }

        var profileResult = await _userResourceService.GetPublicUserProfileByUsernameAsync(username, cancellationToken);
        if (profileResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<PostResponse>>(MapUserProfileError(profileResult.Error));
        }

        var pagination = FeedPaginationSupport.Normalize(request.CursorCreatedAt, request.CursorId, request.Limit);

        var query = _unitOfWork.Repository<Post>()
            .GetAll()
            .AsNoTracking()
            .Where(post =>
                !post.IsDeleted &&
                post.DeletedAt == null &&
                post.UserId == profileResult.Value.UserId);

        if (pagination.HasCursor)
        {
            var createdAt = pagination.CursorCreatedAt!.Value;
            var lastId = pagination.CursorId!.Value;
            query = query.Where(post =>
                (post.CreatedAt < createdAt) ||
                (post.CreatedAt == createdAt && post.Id.CompareTo(lastId) < 0));
        }

        var posts = await query
            .OrderByDescending(post => post.CreatedAt)
            .ThenByDescending(post => post.Id)
            .Take(pagination.Limit)
            .ToListAsync(cancellationToken);

        var response = await FeedPostSupport.ToPostResponsesAsync(
            _unitOfWork,
            _userResourceService,
            request.RequestingUserId,
            posts,
            cancellationToken);

        return Result.Success<IReadOnlyList<PostResponse>>(response);
    }

    private static Error MapUserProfileError(Error error)
    {
        return string.Equals(error.Code, "UserResources.NotFound", StringComparison.Ordinal)
            ? FeedErrors.UserNotFound
            : error;
    }
}
