using Application.Abstractions.Data;
using Application.Configs.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Configs.Queries;

public sealed record GetConfigQuery : IRequest<Result<ConfigResponse>>;

public sealed class GetConfigQueryHandler : IRequestHandler<GetConfigQuery, Result<ConfigResponse>>
{
    private readonly IRepository<Config> _repository;

    public GetConfigQueryHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Config>();
    }

    public async Task<Result<ConfigResponse>> Handle(GetConfigQuery request, CancellationToken cancellationToken)
    {
        var config = await _repository.GetAll()
            .AsNoTracking()
            .Where(item => !item.IsDeleted)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (config == null)
        {
            return Result.Failure<ConfigResponse>(new Error("Config.NotFound", "Config not found"));
        }

        return Result.Success(ConfigMapping.ToResponse(config));
    }
}
