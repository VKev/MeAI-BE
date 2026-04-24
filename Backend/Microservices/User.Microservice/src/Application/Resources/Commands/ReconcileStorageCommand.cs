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

public sealed record ReconcileStorageCommand(
    bool DryRun,
    bool MarkMissingObjects,
    string? Namespace) : IRequest<Result<StorageReconcileResponse>>;

public sealed class ReconcileStorageCommandHandler
    : IRequestHandler<ReconcileStorageCommand, Result<StorageReconcileResponse>>
{
    private readonly IRepository<Resource> _resourceRepository;
    private readonly IObjectStorageService _objectStorageService;

    public ReconcileStorageCommandHandler(
        IUnitOfWork unitOfWork,
        IObjectStorageService objectStorageService)
    {
        _resourceRepository = unitOfWork.Repository<Resource>();
        _objectStorageService = objectStorageService;
    }

    public async Task<Result<StorageReconcileResponse>> Handle(
        ReconcileStorageCommand request,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var namespaceResult = ResolveNamespace(request.Namespace);
        if (namespaceResult.IsFailure)
        {
            return Result.Failure<StorageReconcileResponse>(namespaceResult.Error);
        }

        var storageNamespace = namespaceResult.Value;
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        var listResult = await _objectStorageService.ListAsync(null, cancellationToken);
        if (listResult.IsFailure)
        {
            return Result.Failure<StorageReconcileResponse>(listResult.Error);
        }

        var storageObjects = listResult.Value;
        var storageKeys = storageObjects
            .Select(item => item.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var resources = await _resourceRepository.GetAll()
            .Where(resource => resource.DeletedFromStorageAt == null)
            .ToListAsync(cancellationToken);
        resources = resources
            .Where(resource => IsInNamespace(resource, storageNamespace))
            .ToList();

        var resourceKeys = resources
            .Select(ResolveStorageKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!.TrimStart('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingResources = resources
            .Where(resource =>
            {
                var key = ResolveStorageKey(resource);
                return !string.IsNullOrWhiteSpace(key) && !storageKeys.Contains(key.TrimStart('/'));
            })
            .ToList();

        var markedMissingCount = 0;
        if (request.MarkMissingObjects && !request.DryRun)
        {
            foreach (var resource in missingResources)
            {
                resource.LastVerifiedAt = now;
                resource.DeletedFromStorageAt = now;
                resource.Status = string.IsNullOrWhiteSpace(resource.Status)
                    ? "storage_missing"
                    : resource.Status;
                resource.UpdatedAt = now;
                _resourceRepository.Update(resource);
                markedMissingCount++;
            }
        }

        var verifiedCount = 0;
        if (!request.DryRun)
        {
            foreach (var resource in resources.Except(missingResources))
            {
                resource.LastVerifiedAt = now;
                _resourceRepository.Update(resource);
                verifiedCount++;
            }
        }

        var orphanObjects = storageObjects
            .Where(item => !resourceKeys.Contains(item.Key))
            .ToList();

        return Result.Success(new StorageReconcileResponse(
            request.DryRun,
            storageNamespace,
            resources.Count,
            storageObjects.Count,
            missingResources.Count,
            markedMissingCount,
            verifiedCount,
            orphanObjects.Count,
            orphanObjects.Sum(item => item.SizeBytes),
            missingResources.Take(100).Select(resource => resource.Id.ToString()).ToList(),
            orphanObjects.Take(100).Select(item => item.Key).ToList(),
            errors));
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
