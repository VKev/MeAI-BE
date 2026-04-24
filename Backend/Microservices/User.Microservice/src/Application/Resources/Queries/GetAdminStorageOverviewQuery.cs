using Application.Abstractions.Data;
using Application.Abstractions.Feed;
using Application.Abstractions.Storage;
using Application.Resources.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

namespace Application.Resources.Queries;

public sealed record GetAdminStorageOverviewQuery : IRequest<Result<AdminStorageOverviewResponse>>;

public sealed class GetAdminStorageOverviewQueryHandler
    : IRequestHandler<GetAdminStorageOverviewQuery, Result<AdminStorageOverviewResponse>>
{
    private static readonly IReadOnlyList<(string Key, string Label)> ResourceTypeOrder =
    [
        ("avatar", "Avatar"),
        ("uploaded_image", "Uploaded image"),
        ("uploaded_video", "Uploaded video"),
        ("generated_image", "Generated image"),
        ("generated_video", "Generated video"),
        ("feed_media", "Feed media")
    ];

    private static readonly IReadOnlyList<(string Key, string Label)> ContentTypeOrder =
    [
        ("image/png", "image/png"),
        ("image/jpeg", "image/jpeg"),
        ("video/mp4", "video/mp4")
    ];

    private readonly IRepository<Resource> _resourceRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IObjectStorageService _objectStorageService;
    private readonly IFeedResourceUsageService _feedResourceUsageService;

    public GetAdminStorageOverviewQueryHandler(
        IUnitOfWork unitOfWork,
        IObjectStorageService objectStorageService,
        IFeedResourceUsageService feedResourceUsageService)
    {
        _resourceRepository = unitOfWork.Repository<Resource>();
        _userRepository = unitOfWork.Repository<User>();
        _objectStorageService = objectStorageService;
        _feedResourceUsageService = feedResourceUsageService;
    }

    public async Task<Result<AdminStorageOverviewResponse>> Handle(
        GetAdminStorageOverviewQuery request,
        CancellationToken cancellationToken)
    {
        var resources = await _resourceRepository.GetAll()
            .AsNoTracking()
            .Where(resource => !resource.IsDeleted && resource.DeletedAt == null)
            .ToListAsync(cancellationToken);

        var avatarResourceIds = await _userRepository.GetAll()
            .AsNoTracking()
            .Where(user => !user.IsDeleted && user.AvatarResourceId.HasValue)
            .Select(user => user.AvatarResourceId!.Value)
            .ToHashSetAsync(cancellationToken);

        var feedResourceIds = await GetFeedResourceIdsAsync(cancellationToken);
        var items = new List<StorageItem>(resources.Count);
        var unresolvedFileCount = 0;

        foreach (var resource in resources)
        {
            var sizeBytes = await ResolveSizeBytesAsync(resource, cancellationToken);
            if (!sizeBytes.HasValue)
            {
                unresolvedFileCount++;
            }

            items.Add(new StorageItem(
                ResourceTypeKey: ClassifyResourceType(resource, avatarResourceIds, feedResourceIds),
                ContentTypeKey: NormalizeContentType(resource.ContentType),
                SizeBytes: sizeBytes ?? 0L));
        }

        var totalBytes = items.Sum(item => item.SizeBytes);

        return Result.Success(new AdminStorageOverviewResponse(
            DateTime.UtcNow,
            resources.Count,
            totalBytes,
            ToGb(totalBytes),
            unresolvedFileCount,
            BuildBreakdown(items, item => item.ResourceTypeKey, ResourceTypeOrder),
            BuildBreakdown(items, item => item.ContentTypeKey, ContentTypeOrder)));
    }

    private async Task<IReadOnlySet<Guid>> GetFeedResourceIdsAsync(CancellationToken cancellationToken)
    {
        var result = await _feedResourceUsageService.GetActiveResourceIdsAsync(cancellationToken);
        return result.IsSuccess
            ? result.Value
            : new HashSet<Guid>();
    }

    private async Task<long?> ResolveSizeBytesAsync(Resource resource, CancellationToken cancellationToken)
    {
        if (resource.FileSizeBytes is > 0)
        {
            return resource.FileSizeBytes.Value;
        }

        var metadataResult = await _objectStorageService.GetMetadataAsync(resource.Link, cancellationToken);
        return metadataResult.IsSuccess && metadataResult.Value.ContentLength >= 0
            ? metadataResult.Value.ContentLength
            : null;
    }

    private static string ClassifyResourceType(
        Resource resource,
        IReadOnlySet<Guid> avatarResourceIds,
        IReadOnlySet<Guid> feedResourceIds)
    {
        var resourceType = NormalizeToken(resource.ResourceType);
        var status = NormalizeToken(resource.Status);
        var contentType = NormalizeContentType(resource.ContentType);

        if (resourceType == "avatar" || avatarResourceIds.Contains(resource.Id))
        {
            return "avatar";
        }

        if (feedResourceIds.Contains(resource.Id) ||
            IsFeedResourceType(resourceType) ||
            IsSeedMediaLink(resource.Link))
        {
            return "feed_media";
        }

        var isGenerated = status == "generated";
        var isImage = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                      resourceType.Contains("image", StringComparison.OrdinalIgnoreCase);
        var isVideo = contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
                      resourceType.Contains("video", StringComparison.OrdinalIgnoreCase);

        if (isGenerated && isVideo)
        {
            return "generated_video";
        }

        if (isGenerated && isImage)
        {
            return "generated_image";
        }

        if (isVideo)
        {
            return "uploaded_video";
        }

        if (isImage)
        {
            return "uploaded_image";
        }

        return "other";
    }

    private static IReadOnlyList<AdminStorageBreakdownResponse> BuildBreakdown(
        IReadOnlyList<StorageItem> items,
        Func<StorageItem, string> keySelector,
        IReadOnlyList<(string Key, string Label)> expectedOrder)
    {
        var groups = items
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var results = new List<AdminStorageBreakdownResponse>();
        foreach (var (key, label) in expectedOrder)
        {
            groups.TryGetValue(key, out var groupItems);
            results.Add(ToBreakdown(key, label, groupItems ?? []));
            groups.Remove(key);
        }

        foreach (var (key, groupItems) in groups.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            results.Add(ToBreakdown(key, ToLabel(key), groupItems));
        }

        return results;
    }

    private static AdminStorageBreakdownResponse ToBreakdown(
        string key,
        string label,
        IReadOnlyList<StorageItem> items)
    {
        var totalBytes = items.Sum(item => item.SizeBytes);
        return new AdminStorageBreakdownResponse(
            key,
            label,
            items.Count,
            totalBytes,
            ToGb(totalBytes));
    }

    private static bool IsFeedResourceType(string resourceType)
    {
        return resourceType is "feed" or "feed_media" or "feed-media" or "feedmedia";
    }

    private static bool IsSeedMediaLink(string link)
    {
        return link.Contains("/api/User/seed-media/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeToken(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
    }

    private static string NormalizeContentType(string? contentType)
    {
        return string.IsNullOrWhiteSpace(contentType)
            ? "unknown"
            : contentType.Trim().ToLowerInvariant();
    }

    private static string ToLabel(string key)
    {
        return key == "unknown"
            ? "Unknown"
            : key.Replace('_', ' ');
    }

    private static decimal ToGb(long bytes)
    {
        return decimal.Round(bytes / 1073741824m, 4, MidpointRounding.AwayFromZero);
    }

    private readonly record struct StorageItem(
        string ResourceTypeKey,
        string ContentTypeKey,
        long SizeBytes);
}
