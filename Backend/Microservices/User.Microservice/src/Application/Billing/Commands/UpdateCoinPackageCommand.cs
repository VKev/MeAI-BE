using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
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

    public UpdateCoinPackageCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<CoinPackage>();
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
        entity.Currency = request.Currency.Trim().ToLowerInvariant();
        entity.IsActive = request.IsActive;
        entity.DisplayOrder = request.DisplayOrder;
        entity.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _repository.Update(entity);

        return Result.Success(entity);
    }
}
