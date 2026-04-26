using Application.Abstractions.Data;
using Application.Abstractions.Resources;
using Application.Common;
using Application.Comments.Models;
using Application.Posts.Models;
using Application.Reports.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Reports.Queries;

public sealed record GetAdminReportPreviewQuery(
    Guid ReportId,
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int? Limit,
    Guid RequestingUserId) : IQuery<AdminReportPreviewResponse>;

public sealed class GetAdminReportPreviewQueryHandler : IQueryHandler<GetAdminReportPreviewQuery, AdminReportPreviewResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserResourceService _userResourceService;

    public GetAdminReportPreviewQueryHandler(IUnitOfWork unitOfWork, IUserResourceService userResourceService)
    {
        _unitOfWork = unitOfWork;
        _userResourceService = userResourceService;
    }

    public async Task<Result<AdminReportPreviewResponse>> Handle(GetAdminReportPreviewQuery request, CancellationToken cancellationToken)
    {
        var report = await _unitOfWork.Repository<Report>()
            .GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == request.ReportId, cancellationToken);

        if (report is null)
        {
            return Result.Failure<AdminReportPreviewResponse>(FeedErrors.ReportNotFound);
        }

        return report.TargetType switch
        {
            "Post" => await BuildPostPreviewAsync(report, request.RequestingUserId, cancellationToken),
            "Comment" => await BuildCommentPreviewAsync(report, request, cancellationToken),
            _ => Result.Failure<AdminReportPreviewResponse>(FeedErrors.InvalidReportTarget)
        };
    }

    private async Task<Result<AdminReportPreviewResponse>> BuildPostPreviewAsync(
        Report report,
        Guid requestingUserId,
        CancellationToken cancellationToken)
    {
        var post = await _unitOfWork.Repository<Post>()
            .GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == report.TargetId && !item.IsDeleted && item.DeletedAt == null,
                cancellationToken);

        if (post is null)
        {
            return Result.Failure<AdminReportPreviewResponse>(FeedErrors.PostNotFound);
        }

        var postResponse = await FeedPostSupport.ToPostResponseAsync(
            _unitOfWork,
            _userResourceService,
            requestingUserId,
            post,
            cancellationToken);

        return Result.Success(
            new AdminReportPreviewResponse(
                ReportResponseMapping.ToResponse(report),
                postResponse,
                null));
    }

    private async Task<Result<AdminReportPreviewResponse>> BuildCommentPreviewAsync(
        Report report,
        GetAdminReportPreviewQuery request,
        CancellationToken cancellationToken)
    {
        var pagination = FeedPaginationSupport.Normalize(request.CursorCreatedAt, request.CursorId, request.Limit);

        var targetComment = await _unitOfWork.Repository<Comment>()
            .GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == report.TargetId && !item.IsDeleted && item.DeletedAt == null,
                cancellationToken);

        if (targetComment is null)
        {
            return Result.Failure<AdminReportPreviewResponse>(FeedErrors.CommentNotFound);
        }

        var post = await _unitOfWork.Repository<Post>()
            .GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == targetComment.PostId && !item.IsDeleted && item.DeletedAt == null,
                cancellationToken);

        if (post is null)
        {
            return Result.Failure<AdminReportPreviewResponse>(FeedErrors.PostNotFound);
        }

        Comment? parentComment = null;
        if (targetComment.ParentCommentId.HasValue)
        {
            parentComment = await _unitOfWork.Repository<Comment>()
                .GetAll()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.Id == targetComment.ParentCommentId.Value && !item.IsDeleted && item.DeletedAt == null,
                    cancellationToken);

            if (parentComment is null)
            {
                return Result.Failure<AdminReportPreviewResponse>(FeedErrors.CommentNotFound);
            }
        }

        var siblingQuery = _unitOfWork.Repository<Comment>()
            .GetAll()
            .AsNoTracking()
            .Where(item =>
                item.PostId == targetComment.PostId &&
                item.ParentCommentId == targetComment.ParentCommentId &&
                !item.IsDeleted &&
                item.DeletedAt == null);

        if (pagination.HasCursor)
        {
            var createdAt = pagination.CursorCreatedAt!.Value;
            var lastId = pagination.CursorId!.Value;
            siblingQuery = siblingQuery.Where(comment =>
                (comment.CreatedAt < createdAt) ||
                (comment.CreatedAt == createdAt && comment.Id.CompareTo(lastId) < 0));
        }

        var siblingComments = await siblingQuery
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(pagination.Limit)
            .ToListAsync(cancellationToken);

        bool? CanDelete(Comment comment)
        {
            return comment.UserId == request.RequestingUserId || post.UserId == request.RequestingUserId;
        }

        var targetCommentResponse = await MapSingleCommentAsync(targetComment, request.RequestingUserId, CanDelete, cancellationToken);
        var parentCommentResponse = parentComment is null
            ? null
            : await MapSingleCommentAsync(parentComment, request.RequestingUserId, CanDelete, cancellationToken);

        var siblingResponses = await FeedPostSupport.ToCommentResponsesAsync(
            _unitOfWork,
            _userResourceService,
            request.RequestingUserId,
            siblingComments,
            CanDelete,
            cancellationToken);

        return Result.Success(
            new AdminReportPreviewResponse(
                ReportResponseMapping.ToResponse(report),
                null,
                new AdminReportCommentPreviewResponse(
                    targetCommentResponse,
                    parentCommentResponse,
                    siblingResponses)));
    }

    private async Task<CommentResponse> MapSingleCommentAsync(
        Comment comment,
        Guid requestingUserId,
        Func<Comment, bool?> canDeleteFactory,
        CancellationToken cancellationToken)
    {
        var responses = await FeedPostSupport.ToCommentResponsesAsync(
            _unitOfWork,
            _userResourceService,
            requestingUserId,
            new[] { comment },
            canDeleteFactory,
            cancellationToken);

        return responses[0];
    }
}
