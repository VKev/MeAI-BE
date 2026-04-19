using Application.Abstractions.Data;
using Application.Common;
using Application.Reports.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Reports.Queries;

public sealed record GetMyReportsQuery(
    Guid ReporterId,
    string? Status,
    string? TargetType) : IQuery<IReadOnlyList<ReportResponse>>;

public sealed class GetMyReportsQueryHandler : IQueryHandler<GetMyReportsQuery, IReadOnlyList<ReportResponse>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetMyReportsQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IReadOnlyList<ReportResponse>>> Handle(GetMyReportsQuery request, CancellationToken cancellationToken)
    {
        var normalizedStatus = request.Status is null ? null : FeedModerationSupport.NormalizeStatus(request.Status);
        if (request.Status is not null && normalizedStatus is null)
        {
            return Result.Failure<IReadOnlyList<ReportResponse>>(FeedErrors.InvalidReportStatus);
        }

        var normalizedTargetType = request.TargetType is null ? null : FeedModerationSupport.NormalizeTargetType(request.TargetType);
        if (request.TargetType is not null && normalizedTargetType is null)
        {
            return Result.Failure<IReadOnlyList<ReportResponse>>(FeedErrors.InvalidReportTarget);
        }

        var query = _unitOfWork.Repository<Report>()
            .GetAll()
            .AsNoTracking()
            .Where(item => item.ReporterId == request.ReporterId);

        if (normalizedStatus is not null)
        {
            query = query.Where(item => item.Status == normalizedStatus);
        }

        if (normalizedTargetType is not null)
        {
            query = query.Where(item => item.TargetType == normalizedTargetType);
        }

        var reports = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<ReportResponse>>(reports.Select(ReportResponseMapping.ToResponse).ToList());
    }
}
