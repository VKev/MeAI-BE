using Application.Abstractions.Data;
using Application.Reports.Models;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Reports.Queries;

public sealed record GetAdminReportsQuery : IQuery<IReadOnlyList<ReportResponse>>;

public sealed class GetAdminReportsQueryHandler : IQueryHandler<GetAdminReportsQuery, IReadOnlyList<ReportResponse>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetAdminReportsQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IReadOnlyList<ReportResponse>>> Handle(GetAdminReportsQuery request, CancellationToken cancellationToken)
    {
        var reports = await _unitOfWork.Repository<Report>()
            .GetAll()
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .ToListAsync(cancellationToken);

        var response = reports.Select(ReportResponseMapping.ToResponse).ToList();
        return Result.Success<IReadOnlyList<ReportResponse>>(response);
    }
}
