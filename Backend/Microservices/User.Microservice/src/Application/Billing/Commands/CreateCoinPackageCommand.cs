using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using Microsoft.Extensions.Options;
using SharedLibrary.Configs;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Billing.Commands;

public sealed record CreateCoinPackageCommand(
    string Name,
    decimal CoinAmount,
    decimal BonusCoins,
    decimal Price,
    string Currency,
    bool IsActive,
    int DisplayOrder) : IRequest<Result<CoinPackage>>;

public sealed class CreateCoinPackageCommandHandler
    : IRequestHandler<CreateCoinPackageCommand, Result<CoinPackage>>
{
    private readonly IRepository<CoinPackage> _repository;
    private readonly string _configuredCurrency;

    public CreateCoinPackageCommandHandler(IUnitOfWork unitOfWork, IOptions<BillingCurrencyOptions> billingCurrencyOptions)
    {
        _repository = unitOfWork.Repository<CoinPackage>();
        _configuredCurrency = ResolveCurrency(billingCurrencyOptions.Value);
    }

    public async Task<Result<CoinPackage>> Handle(CreateCoinPackageCommand request, CancellationToken cancellationToken)
    {
        var entity = new CoinPackage
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name.Trim(),
            CoinAmount = request.CoinAmount,
            BonusCoins = request.BonusCoins,
            Price = request.Price,
            Currency = _configuredCurrency,
            IsActive = request.IsActive,
            DisplayOrder = request.DisplayOrder,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _repository.AddAsync(entity, cancellationToken);
        return Result.Success(entity);
    }

    private static string ResolveCurrency(BillingCurrencyOptions options)
    {
        return string.IsNullOrWhiteSpace(options.Currency)
            ? "vnd"
            : options.Currency.Trim().ToLowerInvariant();
    }
}
