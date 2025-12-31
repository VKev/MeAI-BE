namespace Application.Abstractions.Security;

public interface IVerificationCodeStore
{
    Task StoreAsync(string purpose, string email, string code, TimeSpan ttl,
        CancellationToken cancellationToken = default);

    Task<bool> ValidateAsync(string purpose, string email, string code,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(string purpose, string email, CancellationToken cancellationToken = default);
}
