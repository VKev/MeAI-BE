namespace Application.Abstractions.Kie;

public interface IKieAccountService
{
    Task<KieCreditBalanceResult> GetCreditBalanceAsync(CancellationToken cancellationToken = default);
}

public sealed record KieCreditBalanceResult(
    bool Success,
    int Code,
    string Message,
    decimal? RemainingCredits);
