namespace Application.Resources.Models;

public sealed record StorageCleanupResponse(
    bool DryRun,
    string? Namespace,
    int ExpiredResourceCandidateCount,
    long ExpiredResourceCandidateBytes,
    int ExpiredResourceDeletedCount,
    long ExpiredResourceDeletedBytes,
    int OrphanObjectCandidateCount,
    long OrphanObjectCandidateBytes,
    int OrphanObjectDeletedCount,
    long OrphanObjectDeletedBytes,
    IReadOnlyList<string> Errors);

public sealed record StorageReconcileResponse(
    bool DryRun,
    string? Namespace,
    int DatabaseResourceCount,
    int StorageObjectCount,
    int MissingObjectCount,
    int MarkedMissingCount,
    int VerifiedResourceCount,
    int OrphanObjectCount,
    long OrphanObjectBytes,
    IReadOnlyList<string> MissingResourceIds,
    IReadOnlyList<string> OrphanObjectKeys,
    IReadOnlyList<string> Errors);
