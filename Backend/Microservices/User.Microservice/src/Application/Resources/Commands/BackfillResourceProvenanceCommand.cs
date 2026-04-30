using Application.Abstractions.Data;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Resources.Commands;

public sealed record BackfillResourceProvenanceCommand(
    IReadOnlyList<ResourceProvenanceBackfillItem> Items) : IRequest<Result<int>>;

public sealed record ResourceProvenanceBackfillItem(
    Guid ResourceId,
    string? OriginKind,
    string? OriginSourceUrl,
    Guid? OriginChatSessionId,
    Guid? OriginChatId);

public sealed class BackfillResourceProvenanceCommandHandler
    : IRequestHandler<BackfillResourceProvenanceCommand, Result<int>>
{
    private readonly IRepository<Resource> _repository;

    public BackfillResourceProvenanceCommandHandler(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.Repository<Resource>();
    }

    public async Task<Result<int>> Handle(
        BackfillResourceProvenanceCommand request,
        CancellationToken cancellationToken)
    {
        var items = request.Items
            .Where(item => item.ResourceId != Guid.Empty)
            .GroupBy(item => item.ResourceId)
            .Select(group => group.First())
            .ToList();

        if (items.Count == 0)
        {
            return Result.Success(0);
        }

        var resourceIds = items.Select(item => item.ResourceId).ToList();
        var resources = await _repository.GetAll()
            .Where(resource => resourceIds.Contains(resource.Id))
            .ToListAsync(cancellationToken);

        if (resources.Count == 0)
        {
            return Result.Success(0);
        }

        var itemsById = items.ToDictionary(item => item.ResourceId);
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var updatedCount = 0;

        foreach (var resource in resources)
        {
            if (!itemsById.TryGetValue(resource.Id, out var item))
            {
                continue;
            }

            var changed = false;

            if (string.IsNullOrWhiteSpace(resource.OriginKind) && !string.IsNullOrWhiteSpace(item.OriginKind))
            {
                resource.OriginKind = item.OriginKind.Trim();
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(resource.OriginSourceUrl) && !string.IsNullOrWhiteSpace(item.OriginSourceUrl))
            {
                resource.OriginSourceUrl = item.OriginSourceUrl.Trim();
                changed = true;
            }

            if (!resource.OriginChatSessionId.HasValue && item.OriginChatSessionId.HasValue && item.OriginChatSessionId != Guid.Empty)
            {
                resource.OriginChatSessionId = item.OriginChatSessionId.Value;
                changed = true;
            }

            if (!resource.OriginChatId.HasValue && item.OriginChatId.HasValue && item.OriginChatId != Guid.Empty)
            {
                resource.OriginChatId = item.OriginChatId.Value;
                changed = true;
            }

            if (!changed)
            {
                continue;
            }

            resource.UpdatedAt = now;
            _repository.Update(resource);
            updatedCount++;
        }

        return Result.Success(updatedCount);
    }
}
