using Application.Abstractions.Data;
using Application.Abstractions.Resources;
using Application.Common;
using Application.Comments.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Comments.Queries;

public sealed record GetCommentRepliesQuery(
    Guid CommentId,
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int? Limit,
    Guid? RequestingUserId) : IQuery<IReadOnlyList<CommentResponse>>;

public sealed class GetCommentRepliesQueryHandler : IQueryHandler<GetCommentRepliesQuery, IReadOnlyList<CommentResponse>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserResourceService _userResourceService;

    public GetCommentRepliesQueryHandler(IUnitOfWork unitOfWork, IUserResourceService userResourceService)
    {
        _unitOfWork = unitOfWork;
        _userResourceService = userResourceService;
    }

    public async Task<Result<IReadOnlyList<CommentResponse>>> Handle(GetCommentRepliesQuery request, CancellationToken cancellationToken)
    {
        var pagination = FeedPaginationSupport.Normalize(request.CursorCreatedAt, request.CursorId, request.Limit);

        var parentComment = await _unitOfWork.Repository<Comment>()
            .GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == request.CommentId && !item.IsDeleted && item.DeletedAt == null, cancellationToken);

        if (parentComment is null)
        {
            return Result.Failure<IReadOnlyList<CommentResponse>>(FeedErrors.CommentNotFound);
        }

        var post = await _unitOfWork.Repository<Post>()
            .GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == parentComment.PostId && !item.IsDeleted && item.DeletedAt == null, cancellationToken);

        if (post is null)
        {
            return Result.Failure<IReadOnlyList<CommentResponse>>(FeedErrors.PostNotFound);
        }

        var query = _unitOfWork.Repository<Comment>()
            .GetAll()
            .AsNoTracking()
            .Where(item =>
                item.ParentCommentId == request.CommentId &&
                !item.IsDeleted &&
                item.DeletedAt == null);

        if (pagination.HasCursor)
        {
            var createdAt = pagination.CursorCreatedAt!.Value;
            var lastId = pagination.CursorId!.Value;
            query = query.Where(comment =>
                (comment.CreatedAt < createdAt) ||
                (comment.CreatedAt == createdAt && comment.Id.CompareTo(lastId) < 0));
        }

        var replies = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(pagination.Limit)
            .ToListAsync(cancellationToken);

        var response = await FeedPostSupport.ToCommentResponsesAsync(
            _unitOfWork,
            _userResourceService,
            request.RequestingUserId,
            replies,
            comment => request.RequestingUserId.HasValue
                ? comment.UserId == request.RequestingUserId.Value || post.UserId == request.RequestingUserId.Value
                : null,
            cancellationToken);

        return Result.Success<IReadOnlyList<CommentResponse>>(response);
    }
}
