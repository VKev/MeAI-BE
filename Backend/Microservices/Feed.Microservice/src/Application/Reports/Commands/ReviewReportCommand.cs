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

    public ReviewReportCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
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

        if (!string.Equals(action, FeedModerationSupport.NoAction, StringComparison.Ordinal))
        {
            if (!string.Equals(status, FeedModerationSupport.ResolvedStatus, StringComparison.Ordinal))
            {
                return Result.Failure<ReportResponse>(FeedErrors.InvalidReportActionRequiresResolved(action));
            }

            if (!string.Equals(report.TargetType, "Post", StringComparison.Ordinal))
            {
                return Result.Failure<ReportResponse>(FeedErrors.InvalidReportActionForTarget(action, report.TargetType));
            }

            if (string.Equals(action, FeedModerationSupport.DeleteTargetPostAction, StringComparison.Ordinal))
            {
                var post = await _unitOfWork.Repository<Post>()
                    .GetAll()
                    .FirstOrDefaultAsync(item => item.Id == report.TargetId && !item.IsDeleted && item.DeletedAt == null, cancellationToken);

                if (post is null)
                {
                    return Result.Failure<ReportResponse>(FeedErrors.PostNotFound);
                }

                await FeedModerationSupport.SoftDeletePostAsync(_unitOfWork, post, cancellationToken);
            }
            else
            {
                return Result.Failure<ReportResponse>(FeedErrors.InvalidReportActionForTarget(action, report.TargetType));
            }
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        report.Status = status;
        report.ReviewedByAdminId = request.AdminUserId;
        report.ReviewedAt = now;
        report.ResolutionNote = resolutionNote;
        report.ActionType = action;
        report.UpdatedAt = now;
        _unitOfWork.Repository<Report>().Update(report);

        return Result.Success(ReportResponseMapping.ToResponse(report));
    }
}
