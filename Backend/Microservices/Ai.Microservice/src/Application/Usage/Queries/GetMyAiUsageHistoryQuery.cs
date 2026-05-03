using Application.Usage.Models;
using Domain.Repositories;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace Application.Usage.Queries;

public sealed record GetMyAiUsageHistoryQuery(Guid UserId, AiUsageHistoryFilter Filter)
    : IRequest<Result<AiUsageHistoryResponse>>;

public sealed class GetMyAiUsageHistoryQueryHandler
    : IRequestHandler<GetMyAiUsageHistoryQuery, Result<AiUsageHistoryResponse>>
{
    private readonly IAiSpendRecordRepository _aiSpendRecordRepository;
    private readonly IAiUsageTimingResolver _aiUsageTimingResolver;

    public GetMyAiUsageHistoryQueryHandler(
        IAiSpendRecordRepository aiSpendRecordRepository,
        IAiUsageTimingResolver aiUsageTimingResolver)
    {
        _aiSpendRecordRepository = aiSpendRecordRepository;
        _aiUsageTimingResolver = aiUsageTimingResolver;
    }

    public async Task<Result<AiUsageHistoryResponse>> Handle(
        GetMyAiUsageHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var queryResult = AiUsageHistoryQueryFactory.Create(request.Filter with { UserId = request.UserId });
        if (queryResult.IsFailure)
        {
            return queryResult is IValidationResult validationResult
                ? ValidationResult<AiUsageHistoryResponse>.WithErrors(validationResult.Errors)
                : Result.Failure<AiUsageHistoryResponse>(queryResult.Error);
        }

        var page = await _aiSpendRecordRepository.GetHistoryAsync(queryResult.Value, cancellationToken);
        var timings = await _aiUsageTimingResolver.ResolveAsync(page.Items, cancellationToken);

        return Result.Success(AiUsageHistoryMapping.ToResponse(page, timings));
    }
}
