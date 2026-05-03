using Application.Abstractions.Data;
using Application.Billing.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Billing.Queries;

public sealed record GetCoinPackagesQuery() : IRequest<Result<List<CoinPackageResponse>>>;

public sealed class GetCoinPackagesQueryHandler
    : IRequestHandler<GetCoinPackagesQuery, Result<List<CoinPackageResponse>>>
{
    private readonly IRepository<CoinPackage> _coinPackageRepository;

    public GetCoinPackagesQueryHandler(IUnitOfWork unitOfWork)
    {
        _coinPackageRepository = unitOfWork.Repository<CoinPackage>();
    }

    public async Task<Result<List<CoinPackageResponse>>> Handle(
        GetCoinPackagesQuery request,
        CancellationToken cancellationToken)
    {
        var packages = await _coinPackageRepository.GetAll()
            .AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.CreatedAt)
            .Select(item => new CoinPackageResponse(
                item.Id,
                item.Name,
                item.CoinAmount,
                item.BonusCoins,
                item.CoinAmount + item.BonusCoins,
                item.Price,
                item.Currency,
                item.DisplayOrder))
            .ToListAsync(cancellationToken);

        return Result.Success(packages);
    }
}

public sealed record GetAdminCoinPackagesQuery() : IRequest<Result<List<AdminCoinPackageResponse>>>;

public sealed class GetAdminCoinPackagesQueryHandler
    : IRequestHandler<GetAdminCoinPackagesQuery, Result<List<AdminCoinPackageResponse>>>
{
    private readonly IRepository<CoinPackage> _coinPackageRepository;

    public GetAdminCoinPackagesQueryHandler(IUnitOfWork unitOfWork)
    {
        _coinPackageRepository = unitOfWork.Repository<CoinPackage>();
    }

    public async Task<Result<List<AdminCoinPackageResponse>>> Handle(
        GetAdminCoinPackagesQuery request,
        CancellationToken cancellationToken)
    {
        var packages = await _coinPackageRepository.GetAll()
            .AsNoTracking()
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.CreatedAt)
            .Select(item => new AdminCoinPackageResponse(
                item.Id,
                item.Name,
                item.CoinAmount,
                item.BonusCoins,
                item.CoinAmount + item.BonusCoins,
                item.Price,
                item.Currency,
                item.IsActive,
                item.DisplayOrder,
                item.CreatedAt,
                item.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(packages);
    }
}
