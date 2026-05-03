using System.Text.Json;
using Application.Billing;
using Application.Usage.Models;
using Domain.Entities;
using Domain.Repositories;

namespace Application.Usage;

public sealed class AiUsageTimingResolver : IAiUsageTimingResolver
{
    private static readonly AiUsageTiming NullTiming = new(null, null, null);

    private readonly IChatRepository _chatRepository;
    private readonly IImageTaskRepository _imageTaskRepository;
    private readonly IVideoTaskRepository _videoTaskRepository;

    public AiUsageTimingResolver(
        IChatRepository chatRepository,
        IImageTaskRepository imageTaskRepository,
        IVideoTaskRepository videoTaskRepository)
    {
        _chatRepository = chatRepository;
        _imageTaskRepository = imageTaskRepository;
        _videoTaskRepository = videoTaskRepository;
    }

    public async Task<IReadOnlyDictionary<Guid, AiUsageTiming>> ResolveAsync(
        IReadOnlyList<AiSpendRecord> records,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0)
        {
            return new Dictionary<Guid, AiUsageTiming>();
        }

        var results = records.ToDictionary(record => record.Id, _ => NullTiming);
        var imageRecords = records
            .Where(record => string.Equals(record.ReferenceType, CoinReferenceTypes.ChatImage, StringComparison.Ordinal))
            .ToList();
        var videoRecords = records
            .Where(record => string.Equals(record.ReferenceType, CoinReferenceTypes.ChatVideo, StringComparison.Ordinal))
            .ToList();

        var chatIds = imageRecords
            .Concat(videoRecords)
            .Select(record => TryParseGuid(record.ReferenceId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var chats = await _chatRepository.GetByIdsAsync(chatIds, cancellationToken);
        var chatsById = chats.ToDictionary(chat => chat.Id);

        await MapImageTimingsAsync(imageRecords, chatsById, results, cancellationToken);
        await MapVideoTimingsAsync(videoRecords, chatsById, results, cancellationToken);

        return results;
    }

    private async Task MapImageTimingsAsync(
        IReadOnlyList<AiSpendRecord> imageRecords,
        IReadOnlyDictionary<Guid, Chat> chatsById,
        Dictionary<Guid, AiUsageTiming> results,
        CancellationToken cancellationToken)
    {
        var correlationIds = imageRecords
            .Select(record => TryResolveCorrelationId(record, chatsById))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var tasks = await _imageTaskRepository.GetByCorrelationIdsAsync(correlationIds, cancellationToken);
        var taskByCorrelationId = tasks.ToDictionary(task => task.CorrelationId);

        foreach (var record in imageRecords)
        {
            var correlationId = TryResolveCorrelationId(record, chatsById);
            if (!correlationId.HasValue || !taskByCorrelationId.TryGetValue(correlationId.Value, out var task))
            {
                continue;
            }

            results[record.Id] = ToTiming(task.CreatedAt, task.CompletedAt);
        }
    }

    private async Task MapVideoTimingsAsync(
        IReadOnlyList<AiSpendRecord> videoRecords,
        IReadOnlyDictionary<Guid, Chat> chatsById,
        Dictionary<Guid, AiUsageTiming> results,
        CancellationToken cancellationToken)
    {
        var correlationIds = videoRecords
            .Select(record => TryResolveCorrelationId(record, chatsById))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var tasks = await _videoTaskRepository.GetByCorrelationIdsAsync(correlationIds, cancellationToken);
        var taskByCorrelationId = tasks.ToDictionary(task => task.CorrelationId);

        foreach (var record in videoRecords)
        {
            var correlationId = TryResolveCorrelationId(record, chatsById);
            if (!correlationId.HasValue || !taskByCorrelationId.TryGetValue(correlationId.Value, out var task))
            {
                continue;
            }

            results[record.Id] = ToTiming(task.CreatedAt, task.CompletedAt);
        }
    }

    private static Guid? TryResolveCorrelationId(
        AiSpendRecord record,
        IReadOnlyDictionary<Guid, Chat> chatsById)
    {
        var chatId = TryParseGuid(record.ReferenceId);
        if (!chatId.HasValue || !chatsById.TryGetValue(chatId.Value, out var chat))
        {
            return null;
        }

        return TryReadCorrelationId(chat.Config);
    }

    private static Guid? TryReadCorrelationId(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(configJson);
            if (!document.RootElement.TryGetProperty("CorrelationId", out var correlationElement) &&
                !document.RootElement.TryGetProperty("correlationId", out correlationElement))
            {
                return null;
            }

            if (correlationElement.ValueKind == JsonValueKind.String &&
                Guid.TryParse(correlationElement.GetString(), out var correlationId))
            {
                return correlationId;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AiUsageTiming ToTiming(DateTime startedAtUtc, DateTime? completedAtUtc)
    {
        if (!completedAtUtc.HasValue || completedAtUtc.Value <= startedAtUtc)
        {
            return new AiUsageTiming(startedAtUtc, completedAtUtc, null);
        }

        return new AiUsageTiming(
            startedAtUtc,
            completedAtUtc,
            (int)Math.Floor((completedAtUtc.Value - startedAtUtc).TotalSeconds));
    }

    private static Guid? TryParseGuid(string? value)
    {
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }
}
