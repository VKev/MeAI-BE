using Application.Posts.Models;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Posts.Queries;

public sealed record GetBatchDashboardSummaryQuery(
    Guid UserId,
    IReadOnlyList<Guid> SocialMediaIds,
    int? PostLimit) : IRequest<Result<List<SocialPlatformDashboardSummaryResponse>>>;

public sealed class GetBatchDashboardSummaryQueryHandler
    : IRequestHandler<GetBatchDashboardSummaryQuery, Result<List<SocialPlatformDashboardSummaryResponse>>>
{
    private static readonly Post? DomainDependency = null;

    private readonly IMediator _mediator;

    public GetBatchDashboardSummaryQueryHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<Result<List<SocialPlatformDashboardSummaryResponse>>> Handle(
        GetBatchDashboardSummaryQuery request,
        CancellationToken cancellationToken)
    {
        _ = DomainDependency;

        if (request.SocialMediaIds.Count == 0)
        {
            return Result.Success(new List<SocialPlatformDashboardSummaryResponse>());
        }

        var tasks = request.SocialMediaIds
            .Distinct()
            .Select(id => _mediator.Send(
                new GetSocialMediaDashboardSummaryQuery(request.UserId, id, request.PostLimit),
                cancellationToken))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        var summaries = results
            .Where(r => r.IsSuccess)
            .Select(r => r.Value)
            .ToList();

        return Result.Success(summaries);
    }
}
