using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
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

    public CreateCoinPackageCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<CoinPackage>();
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
            Currency = request.Currency.Trim().ToLowerInvariant(),
            IsActive = request.IsActive,
            DisplayOrder = request.DisplayOrder,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        };

        await _repository.AddAsync(entity, cancellationToken);
        return Result.Success(entity);
    }
}
