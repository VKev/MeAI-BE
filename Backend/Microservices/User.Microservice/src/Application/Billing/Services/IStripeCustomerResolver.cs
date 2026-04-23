using Domain.Entities;
using SharedLibrary.Common.ResponseModel;

namespace Application.Billing.Services;

public interface IStripeCustomerResolver
{
    Task<Result<StripeCustomerResolution>> ResolveAsync(
        Guid userId,
        bool createIfMissing,
        CancellationToken cancellationToken);
}

public sealed record StripeCustomerResolution(
    User User,
    string StripeCustomerId);
