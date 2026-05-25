using Application.Abstractions.Notifications;
using Application.Abstractions.Data;
using Application.Common;
using Application.Reports.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Reports.Commands;

public sealed record ReviewReportCommand(
    Guid AdminUserId,
    Guid ReportId,
    string Status,
    string? Action,
    string? ResolutionNote) : ICommand<ReportResponse>;

public sealed class ReviewReportCommandHandler : ICommandHandler<ReviewReportCommand, ReportResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFeedNotificationService _feedNotificationService;

    public ReviewReportCommandHandler(
        IUnitOfWork unitOfWork,
        IFeedNotificationService feedNotificationService)
    {
        _unitOfWork = unitOfWork;
        _feedNotificationService = feedNotificationService;
    }

    public async Task<Result<ReportResponse>> Handle(ReviewReportCommand request, CancellationToken cancellationToken)
    {
        var status = FeedModerationSupport.NormalizeStatus(request.Status);
        if (status is null)
        {
            return Result.Failure<ReportResponse>(FeedErrors.InvalidReportStatus);
        }

        var action = FeedModerationSupport.NormalizeAction(request.Action);
        if (string.IsNullOrWhiteSpace(action))
        {
            return Result.Failure<ReportResponse>(FeedErrors.InvalidReportAction);
        }

        var resolutionNote = FeedPostSupport.NormalizeOptionalText(request.ResolutionNote);

        var report = await _unitOfWork.Repository<Report>()
            .GetAll()
            .FirstOrDefaultAsync(item => item.Id == request.ReportId, cancellationToken);

        if (report is null)
        {
            return Result.Failure<ReportResponse>(FeedErrors.ReportNotFound);
        }

        if (!FeedModerationSupport.CanTransition(report.Status, status))
        {
            return Result.Failure<ReportResponse>(FeedErrors.InvalidReportTransition(status));
        }

        ModerationNotificationContext? notificationContext = null;

        if (!string.Equals(action, FeedModerationSupport.NoAction, StringComparison.Ordinal))
        {
            if (!string.Equals(status, FeedModerationSupport.ResolvedStatus, StringComparison.Ordinal))
            {
                return Result.Failure<ReportResponse>(FeedErrors.InvalidReportActionRequiresResolved(action));
            }

            var moderationResult = await ApplyModerationActionAsync(report, action, cancellationToken);
            if (moderationResult.IsFailure)
            {
                return Result.Failure<ReportResponse>(moderationResult.Error);
            }

            notificationContext = moderationResult.Value;
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        report.Status = status;
        report.ReviewedByAdminId = request.AdminUserId;
        report.ReviewedAt = now;
        report.ResolutionNote = resolutionNote;
        report.ActionType = action;
        report.UpdatedAt = now;
        _unitOfWork.Repository<Report>().Update(report);

        if (notificationContext is not null)
        {
            await _feedNotificationService.NotifyModerationActionAsync(
                request.AdminUserId,
                notificationContext.TargetOwnerUserId,
                report.Id,
                report.TargetType,
                report.TargetId,
                notificationContext.PostId,
                notificationContext.CommentId,
                status,
                action,
                resolutionNote,
                notificationContext.Preview,
                cancellationToken);
        }

        return Result.Success(ReportResponseMapping.ToResponse(report));
    }

    private async Task<Result<ModerationNotificationContext>> ApplyModerationActionAsync(
        Report report,
        string action,
        CancellationToken cancellationToken)
    {
        if (string.Equals(action, FeedModerationSupport.DeleteTargetPostAction, StringComparison.Ordinal))
        {
            return await DeleteTargetPostAsync(report, action, cancellationToken);
        }

        if (string.Equals(action, FeedModerationSupport.DeleteTargetCommentAction, StringComparison.Ordinal))
        {
            return await DeleteTargetCommentAsync(report, action, cancellationToken);
        }

        return Result.Failure<ModerationNotificationContext>(FeedErrors.InvalidReportActionForTarget(action, report.TargetType));
    }

    private async Task<Result<ModerationNotificationContext>> DeleteTargetPostAsync(
        Report report,
        string action,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(report.TargetType, "Post", StringComparison.Ordinal))
        {
            return Result.Failure<ModerationNotificationContext>(FeedErrors.InvalidReportActionForTarget(action, report.TargetType));
        }

        var post = await _unitOfWork.Repository<Post>()
            .GetAll()
            .FirstOrDefaultAsync(item => item.Id == report.TargetId && !item.IsDeleted && item.DeletedAt == null, cancellationToken);

        if (post is null)
        {
            return Result.Failure<ModerationNotificationContext>(FeedErrors.PostNotFound);
        }

        var preview = FeedPostSupport.BuildPreview(post.Content);
        await FeedModerationSupport.SoftDeletePostAsync(_unitOfWork, post, cancellationToken);

        return Result.Success(new ModerationNotificationContext(post.UserId, post.Id, null, preview));
    }

    private async Task<Result<ModerationNotificationContext>> DeleteTargetCommentAsync(
        Report report,
        string action,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(report.TargetType, "Comment", StringComparison.Ordinal))
        {
            return Result.Failure<ModerationNotificationContext>(FeedErrors.InvalidReportActionForTarget(action, report.TargetType));
        }

        var comment = await _unitOfWork.Repository<Comment>()
            .GetAll()
            .FirstOrDefaultAsync(item => item.Id == report.TargetId && !item.IsDeleted && item.DeletedAt == null, cancellationToken);

        if (comment is null)
        {
            return Result.Failure<ModerationNotificationContext>(FeedErrors.CommentNotFound);
        }

        var post = await _unitOfWork.Repository<Post>()
            .GetAll()
            .FirstOrDefaultAsync(item => item.Id == comment.PostId && !item.IsDeleted && item.DeletedAt == null, cancellationToken);

        if (post is null)
        {
            return Result.Failure<ModerationNotificationContext>(FeedErrors.PostNotFound);
        }

        var preview = FeedPostSupport.BuildPreview(comment.Content);
        var deletedCount = await FeedModerationSupport.SoftDeleteCommentThreadAsync(_unitOfWork, post, comment, cancellationToken);
        if (deletedCount == 0)
        {
            return Result.Failure<ModerationNotificationContext>(FeedErrors.CommentNotFound);
        }

        return Result.Success(new ModerationNotificationContext(comment.UserId, post.Id, comment.Id, preview));
    }

    private sealed record ModerationNotificationContext(
        Guid TargetOwnerUserId,
        Guid? PostId,
        Guid? CommentId,
        string? Preview);
}
