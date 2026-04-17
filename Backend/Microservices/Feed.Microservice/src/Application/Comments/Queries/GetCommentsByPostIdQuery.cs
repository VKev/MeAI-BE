using Application.Abstractions.Data;
using Application.Common;
using Application.Comments.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Comments.Queries;

public sealed record GetCommentsByPostIdQuery(Guid PostId) : IQuery<IReadOnlyList<CommentResponse>>;

public sealed class GetCommentsByPostIdQueryHandler : IQueryHandler<GetCommentsByPostIdQuery, IReadOnlyList<CommentResponse>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetCommentsByPostIdQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IReadOnlyList<CommentResponse>>> Handle(GetCommentsByPostIdQuery request, CancellationToken cancellationToken)
    {
        var postExists = await _unitOfWork.Repository<Post>()
            .GetAll()
            .AnyAsync(item => item.Id == request.PostId && !item.IsDeleted && item.DeletedAt == null, cancellationToken);

        if (!postExists)
        {
            return Result.Failure<IReadOnlyList<CommentResponse>>(FeedErrors.PostNotFound);
        }

        var comments = await _unitOfWork.Repository<Comment>()
            .GetAll()
            .Where(item => item.PostId == request.PostId && !item.IsDeleted && item.DeletedAt == null)
            .OrderBy(item => item.ParentCommentId.HasValue)
            .ThenBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);

        var response = comments
            .Select(CommentResponseMapping.ToResponse)
            .ToList();

        return Result.Success<IReadOnlyList<CommentResponse>>(response);
    }
}
