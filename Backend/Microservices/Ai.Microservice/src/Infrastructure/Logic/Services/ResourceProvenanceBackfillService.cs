using System.Text.Json;
using Application.Abstractions.Resources;
using Application.Agents;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedLibrary.Common.Resources;

namespace Infrastructure.Logic.Services;

public sealed class ResourceProvenanceBackfillService
{
    private const int BatchSize = 200;

    private readonly MyDbContext _dbContext;
    private readonly IUserResourceService _userResourceService;
    private readonly ILogger<ResourceProvenanceBackfillService> _logger;

    public ResourceProvenanceBackfillService(
        MyDbContext dbContext,
        IUserResourceService userResourceService,
        ILogger<ResourceProvenanceBackfillService> logger)
    {
        _dbContext = dbContext;
        _userResourceService = userResourceService;
        _logger = logger;
    }

    public async Task BackfillAsync(CancellationToken cancellationToken = default)
    {
        var chats = await _dbContext.Chats
            .AsNoTracking()
            .Where(chat => !chat.DeletedAt.HasValue)
            .OrderBy(chat => chat.CreatedAt ?? DateTime.MinValue)
            .ThenBy(chat => chat.Id)
            .ToListAsync(cancellationToken);

        if (chats.Count == 0)
        {
            _logger.LogInformation("Resource provenance backfill skipped: no chats found.");
            return;
        }

        var candidates = new List<ResourceProvenanceBackfillRequest>();
        foreach (var chat in chats)
        {
            candidates.AddRange(ParseGeneratedResourceCandidates(chat));
            candidates.AddRange(ParseImportedResourceCandidates(chat));
        }

        var items = candidates
            .GroupBy(item => item.ResourceId)
            .Select(group => group
                .OrderBy(item => Priority(item.OriginKind))
                .First())
            .ToList();

        if (items.Count == 0)
        {
            _logger.LogInformation("Resource provenance backfill skipped: no AI resource references found.");
            return;
        }

        var updatedCount = 0;
        foreach (var batch in Chunk(items, BatchSize))
        {
            var result = await _userResourceService.BackfillResourceProvenanceAsync(batch, cancellationToken);
            if (result.IsFailure)
            {
                _logger.LogWarning(
                    "Resource provenance backfill batch failed: {Error}",
                    result.Error.Description);
                continue;
            }

            updatedCount += result.Value;
        }

        _logger.LogInformation(
            "Resource provenance backfill finished. Candidates: {CandidateCount}, Updated: {UpdatedCount}",
            items.Count,
            updatedCount);
    }

    private static IEnumerable<ResourceProvenanceBackfillRequest> ParseGeneratedResourceCandidates(Domain.Entities.Chat chat)
    {
        return ParseResourceIds(chat.ResultResourceIds)
            .Select(resourceId => new ResourceProvenanceBackfillRequest(
                resourceId,
                ResourceOriginKinds.AiGenerated,
                null,
                chat.SessionId,
                chat.Id));
    }

    private static IEnumerable<ResourceProvenanceBackfillRequest> ParseImportedResourceCandidates(Domain.Entities.Chat chat)
    {
        var metadata = AgentMessageConfigSerializer.Parse(chat.Config);
        return (metadata.ImportedResourceIds ?? Array.Empty<Guid>())
            .Where(resourceId => resourceId != Guid.Empty)
            .Select(resourceId => new ResourceProvenanceBackfillRequest(
                resourceId,
                ResourceOriginKinds.AiImportedUrl,
                null,
                chat.SessionId,
                chat.Id));
    }

    private static List<Guid> ParseResourceIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var stringIds = JsonSerializer.Deserialize<List<string>>(json);
            if (stringIds is null || stringIds.Count == 0)
            {
                return [];
            }

            return stringIds
                .Select(value => Guid.TryParse(value, out var parsedId) ? parsedId : Guid.Empty)
                .Where(resourceId => resourceId != Guid.Empty)
                .ToList();
        }
        catch (JsonException)
        {
            try
            {
                var guidIds = JsonSerializer.Deserialize<List<Guid>>(json);
                return guidIds?.Where(resourceId => resourceId != Guid.Empty).ToList() ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }

    private static int Priority(string? originKind)
    {
        return string.Equals(originKind, ResourceOriginKinds.AiGenerated, StringComparison.Ordinal)
            ? 0
            : 1;
    }

    private static IEnumerable<IReadOnlyList<ResourceProvenanceBackfillRequest>> Chunk(
        IReadOnlyList<ResourceProvenanceBackfillRequest> items,
        int size)
    {
        for (var index = 0; index < items.Count; index += size)
        {
            yield return items.Skip(index).Take(size).ToList();
        }
    }
}
