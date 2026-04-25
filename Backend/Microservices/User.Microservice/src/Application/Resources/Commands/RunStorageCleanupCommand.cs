using Application.Abstractions.Data;
using Application.Abstractions.Storage;
using Application.Resources.Models;
using Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Application.Resources.Commands;

public sealed record RunStorageCleanupCommand(
    bool DryRun,
    bool DeleteExpiredResources,
    bool DeleteOrphanObjects,
    int OlderThanDays,
    string? Namespace) : IRequest<Result<StorageCleanupResponse>>;

public sealed class RunStorageCleanupCommandHandler
    : IRequestHandler<RunStorageCleanupCommand, Result<StorageCleanupResponse>>
{
    private readonly IRepository<Resource> _resourceRepository;
    private readonly IObjectStorageService _objectStorageService;

    public RunStorageCleanupCommandHandler(
        IUnitOfWork unitOfWork,
        IObjectStorageService objectStorageService)
    {
        _resourceRepository = unitOfWork.Repository<Resource>();
        _objectStorageService = objectStorageService;
    }

    public async Task<Result<StorageCleanupResponse>> Handle(
        RunStorageCleanupCommand request,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var namespaceResult = ResolveNamespace(request.Namespace);
        if (namespaceResult.IsFailure)
        {
            return Result.Failure<StorageCleanupResponse>(namespaceResult.Error);
        }

        var storageNamespace = namespaceResult.Value;
        var olderThanDays = Math.Max(0, request.OlderThanDays);
        var cutoff = DateTimeExtensions.PostgreSqlUtcNow.AddDays(-olderThanDays);
        var now = DateTimeExtensions.PostgreSqlUtcNow;

        var expiredCandidates = new List<Resource>();
        var expiredDeletedCount = 0;
        var expiredDeletedBytes = 0L;

        if (request.DeleteExpiredResources)
        {
            expiredCandidates = await _resourceRepository.GetAll()
                .Where(resource =>
                    resource.IsDeleted &&
                    resource.DeletedFromStorageAt == null &&
                    ((resource.ExpiresAt != null && resource.ExpiresAt <= now) ||
                     (resource.ExpiresAt == null &&
                      resource.DeletedAt != null &&
                      resource.DeletedAt <= cutoff)))
                .ToListAsync(cancellationToken);

            expiredCandidates = expiredCandidates
                .Where(resource => IsInNamespace(resource, storageNamespace))
                .ToList();

            if (!request.DryRun)
            {
                foreach (var resource in expiredCandidates)
                {
                    var key = ResolveStorageKey(resource);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    var deleteResult = await _objectStorageService.DeleteAsync(key, cancellationToken);
                    if (deleteResult.IsFailure)
                    {
                        errors.Add($"{resource.Id}: {deleteResult.Error.Code} - {deleteResult.Error.Description}");
                        continue;
                    }

                    resource.DeletedFromStorageAt = now;
                    resource.LastVerifiedAt = now;
                    resource.UpdatedAt = now;
                    _resourceRepository.Update(resource);
                    expiredDeletedCount++;
                    expiredDeletedBytes += resource.SizeBytes ?? 0L;
                }
            }
        }

        var orphanObjects = new List<StorageObjectInfo>();
        var orphanDeletedCount = 0;
        var orphanDeletedBytes = 0L;

        if (request.DeleteOrphanObjects)
        {
            var listResult = await _objectStorageService.ListAsync(null, cancellationToken);
            if (listResult.IsFailure)
            {
                errors.Add($"{listResult.Error.Code} - {listResult.Error.Description}");
            }
            else
            {
                var trackedKeys = await BuildTrackedKeySetAsync(cancellationToken);
                orphanObjects = listResult.Value
                    .Where(item => !trackedKeys.Contains(item.Key))
                    .ToList();

                if (!request.DryRun)
                {
                    foreach (var orphan in orphanObjects)
                    {
                        var deleteResult = await _objectStorageService.DeleteAsync(orphan.Key, cancellationToken);
                        if (deleteResult.IsFailure)
                        {
                            errors.Add($"{orphan.Key}: {deleteResult.Error.Code} - {deleteResult.Error.Description}");
                            continue;
                        }

                        orphanDeletedCount++;
                        orphanDeletedBytes += orphan.SizeBytes;
                    }
                }
            }
        }

        return Result.Success(new StorageCleanupResponse(
            request.DryRun,
            storageNamespace,
            expiredCandidates.Count,
            expiredCandidates.Sum(resource => resource.SizeBytes ?? 0L),
            expiredDeletedCount,
            expiredDeletedBytes,
            orphanObjects.Count,
            orphanObjects.Sum(item => item.SizeBytes),
            orphanDeletedCount,
            orphanDeletedBytes,
            errors));
    }

    private async Task<HashSet<string>> BuildTrackedKeySetAsync(CancellationToken cancellationToken)
    {
        var resources = await _resourceRepository.GetAll()
            .AsNoTracking()
            .Where(resource => resource.DeletedFromStorageAt == null)
            .Select(resource => new { resource.StorageKey, resource.Link })
            .ToListAsync(cancellationToken);

        return resources
            .Select(item => string.IsNullOrWhiteSpace(item.StorageKey) ? item.Link : item.StorageKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!.TrimStart('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveStorageKey(Resource resource) =>
        string.IsNullOrWhiteSpace(resource.StorageKey)
            ? resource.Link
            : resource.StorageKey;

    private Result<string?> ResolveNamespace(string? requestedNamespace)
    {
        var configuredNamespace = _objectStorageService.CurrentNamespace;
        if (string.IsNullOrWhiteSpace(requestedNamespace))
        {
            return Result.Success(configuredNamespace);
        }

        var normalized = requestedNamespace.Trim().Trim('/');
        if (!string.Equals(normalized, configuredNamespace, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<string?>(
                new Error("Storage.NamespaceMismatch", "Requested namespace must match the configured S3 namespace."));
        }

        return Result.Success(configuredNamespace);
    }

    private static bool IsInNamespace(Resource resource, string? storageNamespace)
    {
        if (string.IsNullOrWhiteSpace(storageNamespace))
        {
            return true;
        }

        var key = ResolveStorageKey(resource)?.TrimStart('/');
        return string.Equals(resource.StorageNamespace, storageNamespace, StringComparison.OrdinalIgnoreCase) ||
               key?.StartsWith($"{storageNamespace}/", StringComparison.OrdinalIgnoreCase) == true;
    }
}
