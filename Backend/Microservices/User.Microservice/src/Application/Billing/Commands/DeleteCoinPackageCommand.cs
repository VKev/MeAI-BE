using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Billing.Commands;

public sealed record DeleteCoinPackageCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteCoinPackageCommandHandler
    : IRequestHandler<DeleteCoinPackageCommand, Result<bool>>
{
    private readonly IRepository<CoinPackage> _repository;

    public DeleteCoinPackageCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<CoinPackage>();
    }

    public async Task<Result<bool>> Handle(DeleteCoinPackageCommand request, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (entity == null)
        {
            return Result.Failure<bool>(new Error("CoinPackage.NotFound", "Coin package not found."));
        }

        entity.IsActive = false;
        entity.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;
        _repository.Update(entity);

        return Result.Success(true);
    }
}
