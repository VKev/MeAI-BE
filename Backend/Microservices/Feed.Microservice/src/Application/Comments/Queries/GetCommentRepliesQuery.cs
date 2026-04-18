using Application.Abstractions.Data;
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
    int? Limit) : IQuery<IReadOnlyList<CommentResponse>>;

public sealed class GetCommentRepliesQueryHandler : IQueryHandler<GetCommentRepliesQuery, IReadOnlyList<CommentResponse>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetCommentRepliesQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IReadOnlyList<CommentResponse>>> Handle(GetCommentRepliesQuery request, CancellationToken cancellationToken)
    {
        var pagination = FeedPaginationSupport.Normalize(request.CursorCreatedAt, request.CursorId, request.Limit);

        var parentCommentExists = await _unitOfWork.Repository<Comment>()
            .GetAll()
            .AsNoTracking()
            .AnyAsync(item => item.Id == request.CommentId && !item.IsDeleted && item.DeletedAt == null, cancellationToken);

        if (!parentCommentExists)
        {
            return Result.Failure<IReadOnlyList<CommentResponse>>(FeedErrors.CommentNotFound);
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

        var comments = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(pagination.Limit)
            .ToListAsync(cancellationToken);

        var response = comments
            .Select(CommentResponseMapping.ToResponse)
            .ToList();

        return Result.Success<IReadOnlyList<CommentResponse>>(response);
    }
}
