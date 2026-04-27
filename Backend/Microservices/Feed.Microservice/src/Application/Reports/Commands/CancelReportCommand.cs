using Application.Abstractions.Data;
using Application.Common;
using Application.Reports.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Reports.Commands;

public sealed record CancelReportCommand(Guid ReporterId, Guid ReportId) : ICommand<ReportResponse>;

public sealed class CancelReportCommandHandler : ICommandHandler<CancelReportCommand, ReportResponse>
{
    private readonly IUnitOfWork _unitOfWork;

    public CancelReportCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ReportResponse>> Handle(CancelReportCommand request, CancellationToken cancellationToken)
    {
        var report = await _unitOfWork.Repository<Report>()
            .GetAll()
            .FirstOrDefaultAsync(item => item.Id == request.ReportId, cancellationToken);

        if (report is null)
        {
            return Result.Failure<ReportResponse>(FeedErrors.ReportNotFound);
        }

        if (report.ReporterId != request.ReporterId)
        {
            return Result.Failure<ReportResponse>(FeedErrors.Forbidden);
        }

        if (!string.Equals(report.Status, FeedModerationSupport.PendingStatus, StringComparison.Ordinal))
        {
            return Result.Failure<ReportResponse>(FeedErrors.InvalidReportTransition(report.Status));
        }

        var now = DateTimeExtensions.PostgreSqlUtcNow;
        report.Status = FeedModerationSupport.DismissedStatus;
        report.ReviewedByAdminId = request.ReporterId;
        report.ReviewedAt = now;
        report.ResolutionNote = "Cancelled by reporter";
        report.ActionType = FeedModerationSupport.NoAction;
        report.UpdatedAt = now;
        _unitOfWork.Repository<Report>().Update(report);

        return Result.Success(ReportResponseMapping.ToResponse(report));
    }
}
