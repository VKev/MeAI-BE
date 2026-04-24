using Application.Abstractions.Data;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Abstractions.Messaging;
using SharedLibrary.Common.ResponseModel;

namespace Application.Analytics.Queries;

public sealed record GetActiveFeedResourceIdsQuery : IQuery<IReadOnlyList<Guid>>;

public sealed class GetActiveFeedResourceIdsQueryHandler
    : IQueryHandler<GetActiveFeedResourceIdsQuery, IReadOnlyList<Guid>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetActiveFeedResourceIdsQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IReadOnlyList<Guid>>> Handle(
        GetActiveFeedResourceIdsQuery request,
        CancellationToken cancellationToken)
    {
        var resourceIdArrays = await _unitOfWork.Repository<Post>()
            .GetAll()
            .AsNoTracking()
            .Where(post => !post.IsDeleted && post.DeletedAt == null)
            .Select(post => post.ResourceIds)
            .ToListAsync(cancellationToken);

        var resourceIds = resourceIdArrays
            .SelectMany(item => item ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        return Result.Success<IReadOnlyList<Guid>>(resourceIds);
    }
}
