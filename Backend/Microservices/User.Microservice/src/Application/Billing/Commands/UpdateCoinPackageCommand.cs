using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using Microsoft.Extensions.Options;
using SharedLibrary.Configs;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Billing.Commands;

public sealed record UpdateCoinPackageCommand(
    Guid Id,
    string Name,
    decimal CoinAmount,
    decimal BonusCoins,
    decimal Price,
    string Currency,
    bool IsActive,
    int DisplayOrder) : IRequest<Result<CoinPackage>>;

public sealed class UpdateCoinPackageCommandHandler
    : IRequestHandler<UpdateCoinPackageCommand, Result<CoinPackage>>
{
    private readonly IRepository<CoinPackage> _repository;
    private readonly string _configuredCurrency;

    public UpdateCoinPackageCommandHandler(IUnitOfWork unitOfWork, IOptions<BillingCurrencyOptions> billingCurrencyOptions)
    {
        _repository = unitOfWork.Repository<CoinPackage>();
        _configuredCurrency = ResolveCurrency(billingCurrencyOptions.Value);
    }

    public async Task<Result<CoinPackage>> Handle(UpdateCoinPackageCommand request, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (entity == null)
        {
            return Result.Failure<CoinPackage>(new Error("CoinPackage.NotFound", "Coin package not found."));
        }

        entity.Name = request.Name.Trim();
        entity.CoinAmount = request.CoinAmount;
        entity.BonusCoins = request.BonusCoins;
        entity.Price = request.Price;
        entity.Currency = _configuredCurrency;
        entity.IsActive = request.IsActive;
        entity.DisplayOrder = request.DisplayOrder;
        entity.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _repository.Update(entity);

        return Result.Success(entity);
    }

    private static string ResolveCurrency(BillingCurrencyOptions options)
    {
        return string.IsNullOrWhiteSpace(options.Currency)
            ? "vnd"
            : options.Currency.Trim().ToLowerInvariant();
    }
}
