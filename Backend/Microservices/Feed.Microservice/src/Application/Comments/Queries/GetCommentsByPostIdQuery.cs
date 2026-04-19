using Application.Abstractions.Data;
using Application.Common;
using Application.Comments.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Comments.Queries;

public sealed record GetCommentsByPostIdQuery(
    Guid PostId,
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int? Limit) : IQuery<IReadOnlyList<CommentResponse>>;

public sealed class GetCommentsByPostIdQueryHandler : IQueryHandler<GetCommentsByPostIdQuery, IReadOnlyList<CommentResponse>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetCommentsByPostIdQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IReadOnlyList<CommentResponse>>> Handle(GetCommentsByPostIdQuery request, CancellationToken cancellationToken)
    {
        var pagination = FeedPaginationSupport.Normalize(request.CursorCreatedAt, request.CursorId, request.Limit);

        var postExists = await _unitOfWork.Repository<Post>()
            .GetAll()
            .AsNoTracking()
            .AnyAsync(item => item.Id == request.PostId && !item.IsDeleted && item.DeletedAt == null, cancellationToken);

        if (!postExists)
        {
            return Result.Failure<IReadOnlyList<CommentResponse>>(FeedErrors.PostNotFound);
        }

        var query = _unitOfWork.Repository<Comment>()
            .GetAll()
            .AsNoTracking()
            .Where(item =>
                item.PostId == request.PostId &&
                item.ParentCommentId == null &&
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
