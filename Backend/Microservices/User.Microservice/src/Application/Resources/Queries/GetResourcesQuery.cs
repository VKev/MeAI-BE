using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Application.Resources.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.Resources;
using SharedLibrary.Common.ResponseModel;

namespace Application.Resources.Queries;

public sealed record GetResourcesQuery(
    Guid UserId,
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int? Limit,
    IReadOnlyList<string>? OriginKinds = null) : IRequest<Result<List<ResourceResponse>>>;

public sealed class GetResourcesQueryHandler
    : IRequestHandler<GetResourcesQuery, Result<List<ResourceResponse>>>
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;
    private static readonly string[] AllowedOriginKinds =
    [
        ResourceOriginKinds.UserUpload,
        ResourceOriginKinds.AiGenerated,
        ResourceOriginKinds.AiImportedUrl,
        ResourceOriginKinds.SocialMediaImported
    ];
    private readonly IRepository<Resource> _repository;
    private readonly IObjectStorageService _objectStorageService;

    public GetResourcesQueryHandler(IUnitOfWork unitOfWork, IObjectStorageService objectStorageService)
    {
        _repository = unitOfWork.Repository<Resource>();
        _objectStorageService = objectStorageService;
    }

    public async Task<Result<List<ResourceResponse>>> Handle(GetResourcesQuery request,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.Limit ?? DefaultPageSize, 1, MaxPageSize);
        var selectedOriginKinds = NormalizeOriginKinds(request.OriginKinds);

        var query = _repository.GetAll()
            .AsNoTracking()
            .Where(resource => resource.UserId == request.UserId && !resource.IsDeleted);

        if (selectedOriginKinds.Count > 0)
        {
            query = query.Where(resource =>
                resource.OriginKind != null &&
                selectedOriginKinds.Contains(resource.OriginKind));
        }

        if (request.CursorCreatedAt.HasValue && request.CursorId.HasValue)
        {
            var createdAt = request.CursorCreatedAt.Value;
            var lastId = request.CursorId.Value;
            query = query.Where(resource =>
                (resource.CreatedAt < createdAt) ||
                (resource.CreatedAt == createdAt && resource.Id.CompareTo(lastId) < 0));
        }

        var resources = await query
            .OrderByDescending(resource => resource.CreatedAt)
            .ThenByDescending(resource => resource.Id)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var response = new List<ResourceResponse>(resources.Count);
        foreach (var resource in resources)
        {
            var presignedResult = _objectStorageService.GetPresignedUrl(resource.Link);
            if (presignedResult.IsFailure)
            {
                return Result.Failure<List<ResourceResponse>>(presignedResult.Error);
            }

            response.Add(ResourceMapping.ToResponse(resource, presignedResult.Value));
        }

        return Result.Success(response);
    }

    private static HashSet<string> NormalizeOriginKinds(IReadOnlyList<string>? originKinds)
    {
        if (originKinds is null || originKinds.Count == 0)
        {
            return [];
        }

        return originKinds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Where(value => AllowedOriginKinds.Contains(value, StringComparer.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }
}
