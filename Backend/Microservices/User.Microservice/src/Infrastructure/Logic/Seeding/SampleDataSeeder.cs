using System.Text.Json;
using Application.Abstractions.Storage;
using Domain.Entities;
using Infrastructure.Configuration;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Logic.Seeding;

public sealed class SampleDataSeeder
{
    private const string ManifestFileName = "manifest.json";
    private const string MediaDirectoryName = "media";
    private const string RuntimeDirectoryName = "runtime";
    private const string StateFileName = "state.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly MyDbContext _dbContext;
    private readonly IObjectStorageService _objectStorageService;
    private readonly DefaultUserSeedOptions _defaultUserOptions;
    private readonly SampleSeedOptions _options;
    private readonly ILogger<SampleDataSeeder> _logger;

    public SampleDataSeeder(
        MyDbContext dbContext,
        IObjectStorageService objectStorageService,
        IOptions<DefaultUserSeedOptions> defaultUserOptions,
        IOptions<SampleSeedOptions> options,
        ILogger<SampleDataSeeder> logger)
    {
        _dbContext = dbContext;
        _objectStorageService = objectStorageService;
        _defaultUserOptions = defaultUserOptions.Value;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Sample data seed skipped: SampleSeed:Enabled is false.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_defaultUserOptions.Email))
        {
            _logger.LogWarning("Sample data seed skipped: DefaultUser:Email is missing.");
            return;
        }

        var dataRoot = ResolveDataRoot(_options.DataRoot);
        var manifestPath = Path.Combine(dataRoot, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            _logger.LogWarning("Sample data seed skipped: manifest file was not found at {ManifestPath}.", manifestPath);
            return;
        }

        var manifest = await LoadManifestAsync(manifestPath, cancellationToken);
        if (manifest is null)
        {
            return;
        }

        var runtimeDirectory = Path.Combine(dataRoot, RuntimeDirectoryName);
        var statePath = Path.Combine(runtimeDirectory, StateFileName);
        Directory.CreateDirectory(runtimeDirectory);
        DeleteStateFileIfExists(statePath);

        var normalizedEmail = _defaultUserOptions.Email.Trim().ToLowerInvariant();
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(
                item => !item.IsDeleted && item.Email.ToLower() == normalizedEmail,
                cancellationToken);

        if (user is null)
        {
            _logger.LogWarning(
                "Sample data seed skipped: default user with email {Email} was not found.",
                _defaultUserOptions.Email);
            return;
        }

        var now = DateTime.UtcNow;
        UpsertWorkspace(user.Id, manifest.Workspace, now);

        var hasFailure = false;
        for (var index = 0; index < manifest.Resources.Count; index++)
        {
            var resourceItem = manifest.Resources[index];
            var filePath = Path.Combine(dataRoot, MediaDirectoryName, resourceItem.FileName);

            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Sample data resource file not found at {FilePath}.", filePath);
                hasFailure = true;
                continue;
            }

            await using var fileStream = File.OpenRead(filePath);
            var storageKey = BuildStorageKey(user.Id, resourceItem.Id);
            var uploadResult = await _objectStorageService.UploadAsync(
                new StorageUploadRequest(
                    storageKey,
                    fileStream,
                    resourceItem.ContentType,
                    fileStream.Length),
                cancellationToken);

            if (uploadResult.IsFailure)
            {
                _logger.LogError(
                    "Failed to upload sample resource {ResourceId}: {Error}",
                    resourceItem.Id,
                    uploadResult.Error.Description);
                hasFailure = true;
                continue;
            }

            var existingResource = await _dbContext.Resources
                .FirstOrDefaultAsync(resource => resource.Id == resourceItem.Id, cancellationToken);

            if (existingResource is null)
            {
                existingResource = new Resource
                {
                    Id = resourceItem.Id,
                    CreatedAt = now.AddMinutes(-(manifest.Resources.Count - index))
                };

                _dbContext.Resources.Add(existingResource);
            }

            existingResource.UserId = user.Id;
            existingResource.Link = uploadResult.Value.Key;
            existingResource.Status = string.IsNullOrWhiteSpace(resourceItem.Status) ? "ready" : resourceItem.Status.Trim();
            existingResource.ResourceType = resourceItem.ResourceType.Trim();
            existingResource.ContentType = resourceItem.ContentType.Trim();
            existingResource.IsDeleted = false;
            existingResource.DeletedAt = null;
            existingResource.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (hasFailure)
        {
            _logger.LogWarning("Sample data seed completed with missing resources; AI sample chats will not be activated.");
            return;
        }

        var state = new SampleSeedState
        {
            UserId = user.Id,
            WorkspaceId = manifest.Workspace.Id,
            ResourceIds = manifest.Resources.Select(item => item.Id).ToList(),
            SeededAtUtc = now
        };

        var json = JsonSerializer.Serialize(state, SerializerOptions);
        await File.WriteAllTextAsync(statePath, json, cancellationToken);

        _logger.LogInformation(
            "Seeded sample user data for {Email} with {ResourceCount} resources and workspace {WorkspaceId}.",
            user.Email,
            manifest.Resources.Count,
            manifest.Workspace.Id);
    }

    private void UpsertWorkspace(Guid userId, SampleSeedWorkspace workspaceItem, DateTime now)
    {
        var existingWorkspace = _dbContext.Workspaces.FirstOrDefault(workspace => workspace.Id == workspaceItem.Id);
        if (existingWorkspace is null)
        {
            existingWorkspace = new Workspace
            {
                Id = workspaceItem.Id,
                CreatedAt = now
            };

            _dbContext.Workspaces.Add(existingWorkspace);
        }

        existingWorkspace.UserId = userId;
        existingWorkspace.Name = workspaceItem.Name.Trim();
        existingWorkspace.Type = NormalizeOptional(workspaceItem.Type);
        existingWorkspace.Description = NormalizeOptional(workspaceItem.Description);
        existingWorkspace.IsDeleted = false;
        existingWorkspace.DeletedAt = null;
        existingWorkspace.UpdatedAt = now;
    }

    private static string ResolveDataRoot(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath("/seed-data");
        }

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
    }

    private static async Task<SampleSeedManifest?> LoadManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(manifestPath);
        return await JsonSerializer.DeserializeAsync<SampleSeedManifest>(stream, SerializerOptions, cancellationToken);
    }

    private static void DeleteStateFileIfExists(string statePath)
    {
        if (File.Exists(statePath))
        {
            File.Delete(statePath);
        }
    }

    private static string BuildStorageKey(Guid userId, Guid resourceId) => $"resources/{userId}/{resourceId}";

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public sealed class SampleSeedManifest
    {
        public required SampleSeedWorkspace Workspace { get; set; }

        public required SampleSeedChatSession ChatSession { get; set; }

        public List<SampleSeedResource> Resources { get; set; } = [];

        public List<SampleSeedChat> Chats { get; set; } = [];
    }

    public sealed class SampleSeedWorkspace
    {
        public Guid Id { get; set; }

        public required string Name { get; set; }

        public string? Type { get; set; }

        public string? Description { get; set; }
    }

    public sealed class SampleSeedChatSession
    {
        public Guid Id { get; set; }

        public required string Name { get; set; }
    }

    public sealed class SampleSeedResource
    {
        public Guid Id { get; set; }

        public required string FileName { get; set; }

        public required string ResourceType { get; set; }

        public required string ContentType { get; set; }

        public string? Status { get; set; }
    }

    public sealed class SampleSeedChat
    {
        public Guid Id { get; set; }

        public required string Prompt { get; set; }

        public List<Guid> ResourceIds { get; set; } = [];
    }

    public sealed class SampleSeedState
    {
        public Guid UserId { get; set; }

        public Guid WorkspaceId { get; set; }

        public List<Guid> ResourceIds { get; set; } = [];

        public DateTime SeededAtUtc { get; set; }
    }
}
