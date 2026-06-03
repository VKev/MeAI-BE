using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Storage;
using Domain.Entities;
using Infrastructure.Configuration;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedLibrary.Authentication;
using SharedLibrary.Common.Resources;

namespace Infrastructure.Logic.Seeding;

public sealed class FeedDemoUserSeeder
{
    private const string RuntimeDirectoryName = "runtime";
    private const string StateFileName = "users.state.json";
    private const string MediaDirectoryName = "media";
    private const string PackagedFeedSeedDataPath = "SeedData/Feed";
    private const string DefaultPassword = "12345678";
    private const string StorageProvider = "s3";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly MyDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IObjectStorageService _objectStorageService;
    private readonly FeedSeedOptions _options;
    private readonly DefaultUserSeedOptions _defaultUserOptions;
    private readonly AdminSeedOptions _adminSeedOptions;
    private readonly ILogger<FeedDemoUserSeeder> _logger;

    public FeedDemoUserSeeder(
        MyDbContext dbContext,
        IPasswordHasher passwordHasher,
        IObjectStorageService objectStorageService,
        IOptions<FeedSeedOptions> options,
        IOptions<DefaultUserSeedOptions> defaultUserOptions,
        IOptions<AdminSeedOptions> adminSeedOptions,
        ILogger<FeedDemoUserSeeder> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _objectStorageService = objectStorageService;
        _options = options.Value;
        _defaultUserOptions = defaultUserOptions.Value;
        _adminSeedOptions = adminSeedOptions.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var dataRoot = ResolveDataRoot(_options.DataRoot);
        var runtimeDirectory = Path.Combine(dataRoot, RuntimeDirectoryName);
        var statePath = Path.Combine(runtimeDirectory, StateFileName);
        Directory.CreateDirectory(runtimeDirectory);
        DeleteStateFileIfExists(statePath);

        if (!_options.Enabled)
        {
            _logger.LogInformation("Feed demo user seed skipped: FeedSeed:Enabled is false.");
            return;
        }

        var mediaRoot = ResolveMediaRoot(dataRoot);
        if (!Directory.Exists(mediaRoot))
        {
            _logger.LogWarning("Feed demo user seed skipped: media directory was not found at {MediaRoot}.", mediaRoot);
            return;
        }

        var mediaFiles = Directory
            .EnumerateFiles(mediaRoot, "*", SearchOption.AllDirectories)
            .Select(filePath =>
            {
                var fullPath = Path.GetFullPath(filePath);
                var fileInfo = new FileInfo(fullPath);
                return new MediaFileDefinition(
                    FullPath: fullPath,
                    RelativePath: Path.GetRelativePath(mediaRoot, fullPath).Replace('\\', '/'),
                    ResourceType: InferResourceType(fullPath),
                    ContentType: InferContentType(fullPath),
                    SizeBytes: fileInfo.Length);
            })
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (mediaFiles.Count == 0)
        {
            _logger.LogWarning("Feed demo user seed skipped: no media files were found in {MediaRoot}.", mediaRoot);
            return;
        }

        var userPlans = BuildUserPlans();
        var ignoredEmails = BuildIgnoredEmailSet();
        if (await HasExistingUserDataAsync(ignoredEmails, cancellationToken))
        {
            if (await TryWriteExistingSeedStateAsync(statePath, userPlans, mediaFiles, cancellationToken))
            {
                _logger.LogInformation(
                    "Feed demo user seed detected existing dataset and refreshed S3-backed state at {StatePath}.",
                    statePath);
            }
            else
            {
                _logger.LogInformation("Feed demo user seed skipped: user data is not empty.");
            }

            return;
        }

        var now = DateTime.UtcNow;
        var password = string.IsNullOrWhiteSpace(_defaultUserOptions.Password)
            ? DefaultPassword
            : _defaultUserOptions.Password.Trim();
        var role = await GetOrCreateUserRoleAsync(now, cancellationToken);

        var users = new List<User>(userPlans.Count);
        var userRoles = new List<UserRole>(userPlans.Count);
        for (var index = 0; index < userPlans.Count; index++)
        {
            var plan = userPlans[index];
            var userId = CreateDeterministicGuid($"feed-seed:user:{plan.Username}");
            var createdAt = now.AddMinutes(-(userPlans.Count - index + 1));

            users.Add(new User
            {
                Id = userId,
                Username = plan.Username,
                PasswordHash = _passwordHasher.HashPassword(password),
                Email = plan.Email,
                FullName = plan.FullName,
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
                EmailVerified = true,
                IsDeleted = false,
                DeletedAt = null,
                AvatarResourceId = null,
                PhoneNumber = null,
                Provider = null,
                Address = null,
                Birthday = null,
                MeAiCoin = 0
            });

            userRoles.Add(new UserRole
            {
                Id = CreateDeterministicGuid($"feed-seed:user-role:{plan.Username}:{role.Id}"),
                UserId = userId,
                RoleId = role.Id,
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
                IsDeleted = false,
                DeletedAt = null
            });
        }

        _dbContext.Users.AddRange(users);
        _dbContext.UserRoles.AddRange(userRoles);

        var mediaRichUsernames = userPlans
            .Where(plan => plan.HasMedia)
            .Select(plan => plan.Username)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var resources = new List<Resource>();
        var stateResources = new List<FeedSeedResourceState>();
        var hasUploadFailure = false;

        foreach (var user in users.Where(item => mediaRichUsernames.Contains(item.Username)))
        {
            foreach (var mediaFile in mediaFiles)
            {
                var resourceId = CreateDeterministicGuid($"feed-seed:resource:{user.Username}:{mediaFile.RelativePath}");
                var createdAt = now.AddMinutes(-(resources.Count + 1));
                var uploadResult = await UploadSeedMediaAsync(user.Id, resourceId, mediaFile, cancellationToken);
                if (uploadResult is null)
                {
                    hasUploadFailure = true;
                    continue;
                }

                var resource = new Resource
                {
                    Id = resourceId,
                    UserId = user.Id,
                    CreatedAt = createdAt
                };

                ApplyUploadedStorage(resource, uploadResult, mediaFile, createdAt);
                resources.Add(resource);
                stateResources.Add(ToStateResource(resource, mediaFile));
            }
        }

        if (hasUploadFailure)
        {
            _logger.LogWarning("Feed demo user seed skipped: one or more media files failed to upload to object storage.");
            return;
        }

        _dbContext.Resources.AddRange(resources);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var state = new FeedSeedState
        {
            SeededAtUtc = now,
            Users = userPlans.Select(ToStateUser).ToList(),
            Resources = stateResources
        };

        var json = JsonSerializer.Serialize(state, SerializerOptions);
        await File.WriteAllTextAsync(statePath, json, cancellationToken);

        _logger.LogInformation(
            "Seeded {UserCount} feed demo users and uploaded {ResourceCount} media resources to object storage. State written to {StatePath}.",
            users.Count,
            resources.Count,
            statePath);
    }

    private async Task<bool> TryWriteExistingSeedStateAsync(
        string statePath,
        IReadOnlyCollection<FeedSeedUserPlan> userPlans,
        IReadOnlyCollection<MediaFileDefinition> mediaFiles,
        CancellationToken cancellationToken)
    {
        var expectedUsers = userPlans
            .Select(plan => new
            {
                Plan = plan,
                UserId = CreateDeterministicGuid($"feed-seed:user:{plan.Username}")
            })
            .ToList();

        var expectedUserIds = expectedUsers.Select(item => item.UserId).ToList();

        var existingUsers = await _dbContext.Users
            .AsNoTracking()
            .Where(user => !user.IsDeleted && expectedUserIds.Contains(user.Id))
            .ToListAsync(cancellationToken);

        if (existingUsers.Count != expectedUsers.Count)
        {
            return false;
        }

        var expectedResources = expectedUsers
            .Where(item => item.Plan.HasMedia)
            .SelectMany(
                item => mediaFiles.Select(mediaFile => new ExpectedFeedSeedResource(
                    Id: CreateDeterministicGuid($"feed-seed:resource:{item.Plan.Username}:{mediaFile.RelativePath}"),
                    UserId: item.UserId,
                    MediaFile: mediaFile)))
            .ToList();

        var expectedResourceIds = expectedResources.Select(item => item.Id).ToList();
        var existingResources = await _dbContext.Resources
            .Where(resource => expectedResourceIds.Contains(resource.Id))
            .ToListAsync(cancellationToken);

        if (existingResources.Count != expectedResourceIds.Count)
        {
            return false;
        }

        var resourcesById = existingResources.ToDictionary(resource => resource.Id);
        var stateResources = new List<FeedSeedResourceState>(expectedResources.Count);
        var hasUploadFailure = false;

        foreach (var expectedResource in expectedResources)
        {
            var resource = resourcesById[expectedResource.Id];
            if (!await EnsureSeedResourceUploadedAsync(
                    resource,
                    expectedResource.UserId,
                    expectedResource.MediaFile,
                    cancellationToken))
            {
                hasUploadFailure = true;
                continue;
            }

            stateResources.Add(ToStateResource(resource, expectedResource.MediaFile));
        }

        if (hasUploadFailure)
        {
            _logger.LogWarning("Feed demo user seed state refresh failed: one or more existing media resources could not be uploaded to object storage.");
            return false;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var state = new FeedSeedState
        {
            SeededAtUtc = DateTime.UtcNow,
            Users = userPlans.Select(ToStateUser).ToList(),
            Resources = stateResources
        };

        var json = JsonSerializer.Serialize(state, SerializerOptions);
        await File.WriteAllTextAsync(statePath, json, cancellationToken);
        return true;
    }

    private async Task<bool> HasExistingUserDataAsync(HashSet<string> ignoredEmails, CancellationToken cancellationToken)
    {
        var hasExistingUsers = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(
                user => !user.IsDeleted && !ignoredEmails.Contains((user.Email ?? string.Empty).ToLower()),
                cancellationToken);

        if (hasExistingUsers)
        {
            return true;
        }

        return await (
                from resource in _dbContext.Resources.AsNoTracking()
                join user in _dbContext.Users.AsNoTracking() on resource.UserId equals user.Id
                where !resource.IsDeleted
                      && !user.IsDeleted
                      && !ignoredEmails.Contains((user.Email ?? string.Empty).ToLower())
                select resource.Id)
            .AnyAsync(cancellationToken);
    }

    private async Task<StorageUploadResult?> UploadSeedMediaAsync(
        Guid userId,
        Guid resourceId,
        MediaFileDefinition mediaFile,
        CancellationToken cancellationToken)
    {
        await using var fileStream = File.OpenRead(mediaFile.FullPath);
        var uploadResult = await _objectStorageService.UploadAsync(
            new StorageUploadRequest(
                BuildStorageKey(userId, resourceId),
                fileStream,
                mediaFile.ContentType,
                fileStream.Length),
            cancellationToken);

        if (uploadResult.IsFailure)
        {
            _logger.LogError(
                "Failed to upload feed demo media {RelativePath} for resource {ResourceId}: {Error}",
                mediaFile.RelativePath,
                resourceId,
                uploadResult.Error.Description);
            return null;
        }

        return uploadResult.Value;
    }

    private async Task<bool> EnsureSeedResourceUploadedAsync(
        Resource resource,
        Guid expectedUserId,
        MediaFileDefinition mediaFile,
        CancellationToken cancellationToken)
    {
        if (!NeedsStorageUpload(resource, mediaFile))
        {
            ApplySeedResourceMetadata(resource, expectedUserId, mediaFile, DateTime.UtcNow);
            return true;
        }

        var uploadResult = await UploadSeedMediaAsync(expectedUserId, resource.Id, mediaFile, cancellationToken);
        if (uploadResult is null)
        {
            return false;
        }

        resource.UserId = expectedUserId;
        ApplyUploadedStorage(resource, uploadResult, mediaFile, DateTime.UtcNow);
        return true;
    }

    private static void ApplyUploadedStorage(
        Resource resource,
        StorageUploadResult uploadResult,
        MediaFileDefinition mediaFile,
        DateTime updatedAt)
    {
        ApplySeedResourceMetadata(resource, resource.UserId, mediaFile, updatedAt);
        resource.Link = uploadResult.Key;
        resource.StorageProvider = StorageProvider;
        resource.StorageBucket = uploadResult.Bucket;
        resource.StorageRegion = uploadResult.Region;
        resource.StorageNamespace = uploadResult.Namespace;
        resource.StorageKey = uploadResult.Key;
        resource.LastVerifiedAt = updatedAt;
        resource.DeletedFromStorageAt = null;
    }

    private static void ApplySeedResourceMetadata(
        Resource resource,
        Guid userId,
        MediaFileDefinition mediaFile,
        DateTime updatedAt)
    {
        resource.UserId = userId;
        resource.Status = "ready";
        resource.ResourceType = mediaFile.ResourceType;
        resource.ContentType = mediaFile.ContentType;
        resource.SizeBytes = mediaFile.SizeBytes;
        resource.OriginalFileName = Path.GetFileName(mediaFile.RelativePath);
        resource.OriginKind = ResourceOriginKinds.UserUpload;
        resource.IsDeleted = false;
        resource.DeletedAt = null;
        resource.UpdatedAt = updatedAt;
    }

    private static bool NeedsStorageUpload(Resource resource, MediaFileDefinition mediaFile)
    {
        if (string.IsNullOrWhiteSpace(resource.Link) || IsSeedMediaLink(resource.Link))
        {
            return true;
        }

        if (!string.Equals(resource.StorageProvider, StorageProvider, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(resource.StorageKey))
        {
            return true;
        }

        if (resource.SizeBytes != mediaFile.SizeBytes)
        {
            return true;
        }

        return !string.Equals(resource.ContentType, mediaFile.ContentType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSeedMediaLink(string link)
    {
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri))
        {
            return uri.AbsolutePath.StartsWith("/api/User/seed-media/", StringComparison.OrdinalIgnoreCase);
        }

        return link.StartsWith("/api/User/seed-media/", StringComparison.OrdinalIgnoreCase) ||
               link.Contains("/api/User/seed-media/", StringComparison.OrdinalIgnoreCase);
    }

    private static FeedSeedResourceState ToStateResource(Resource resource, MediaFileDefinition mediaFile) =>
        new()
        {
            Id = resource.Id,
            UserId = resource.UserId,
            FileName = Path.GetFileName(mediaFile.RelativePath),
            RelativePath = mediaFile.RelativePath,
            ResourceType = mediaFile.ResourceType,
            ContentType = mediaFile.ContentType,
            Link = resource.Link
        };

    private static FeedSeedUserState ToStateUser(FeedSeedUserPlan plan) =>
        new()
        {
            Id = CreateDeterministicGuid($"feed-seed:user:{plan.Username}"),
            Username = plan.Username,
            Email = plan.Email,
            FullName = plan.FullName,
            ProfileKind = plan.ProfileKind,
            HasMedia = plan.HasMedia
        };

    private static void DeleteStateFileIfExists(string statePath)
    {
        if (File.Exists(statePath))
        {
            File.Delete(statePath);
        }
    }

    private static string BuildStorageKey(Guid userId, Guid resourceId) => $"resources/{userId}/{resourceId}";

    private async Task<Role> GetOrCreateUserRoleAsync(DateTime now, CancellationToken cancellationToken)
    {
        const string roleName = "USER";

        var role = await _dbContext.Roles
            .FirstOrDefaultAsync(item => !item.IsDeleted && item.Name == roleName, cancellationToken);

        if (role is not null)
        {
            return role;
        }

        role = new Role
        {
            Id = CreateDeterministicGuid("feed-seed:role:user"),
            Name = roleName,
            Description = "Standard user",
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false,
            DeletedAt = null
        };

        _dbContext.Roles.Add(role);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return role;
    }

    private HashSet<string> BuildIgnoredEmailSet()
    {
        var values = new[]
        {
            _defaultUserOptions.Email,
            _adminSeedOptions.Email
        };

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static List<FeedSeedUserPlan> BuildUserPlans()
    {
        return new List<FeedSeedUserPlan>
        {
            new("maya_canvas", "maya.canvas+seed@meai.local", "Maya Canvas", "hub", true),
            new("leo_travelnotes", "leo.travelnotes+seed@meai.local", "Leo Travel Notes", "hub", true),
            new("sora_frames", "sora.frames+seed@meai.local", "Sora Frames", "media", true),
            new("iris_motion", "iris.motion+seed@meai.local", "Iris Motion", "media", true),
            new("nora_bookclub", "nora.bookclub+seed@meai.local", "Nora Book Club", "storyteller", true),
            new("quang_nomad", "quang.nomad+seed@meai.local", "Quang Nomad", "storyteller", true),
            new("vera_grid", "vera.grid+seed@meai.local", "Vera Grid", "designer", true),
            new("zane_looplab", "zane.looplab+seed@meai.local", "Zane Loop Lab", "designer", true),
            new("linh_overflow_test", "linh.overflow+seed@meai.local", "Linh với một cái tên hiển thị rất dài để test card trên mobile", "balanced", false),
            new("otto_smalltalk", "otto.smalltalk+seed@meai.local", "Otto Smalltalk", "balanced", false),
            new("mina_unicode", "mina.unicode+seed@meai.local", "Mina Unicode ミナ ユニコード", "balanced", false),
            new("kai_newline", "kai.newline+seed@meai.local", "Kai Newline", "balanced", false),
            new("hana_numbers", "hana.numbers+seed@meai.local", "Hana Numbers 123", "balanced", false),
            new("bao_capsule", "bao.capsule+seed@meai.local", "Bảo Capsule", "balanced", true),
            new("ria_quietmode", "ria.quietmode+seed@meai.local", "Ria Quiet Mode", "quiet", false),
            new("tuan_minimal", "tuan.minimal+seed@meai.local", "Tuấn Minimal", "quiet", true),
            new("yuki_firstday", "yuki.firstday+seed@meai.local", "Yuki First Day", "newcomer", false),
            new("pax_reader", "pax.reader+seed@meai.local", "Pax Reader", "observer", false)
        };
    }

    private static string ResolveDataRoot(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath("/seed-data/feed");
        }

        return Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
    }

    private static string ResolveMediaRoot(string dataRoot)
    {
        var configuredMediaRoot = Path.GetFullPath(Path.Combine(dataRoot, MediaDirectoryName));
        if (HasMediaFiles(configuredMediaRoot))
        {
            return configuredMediaRoot;
        }

        var packagedMediaRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, PackagedFeedSeedDataPath, MediaDirectoryName));
        return HasMediaFiles(packagedMediaRoot) ? packagedMediaRoot : configuredMediaRoot;
    }

    private static bool HasMediaFiles(string mediaRoot)
    {
        return Directory.Exists(mediaRoot) &&
               Directory.EnumerateFiles(mediaRoot, "*", SearchOption.AllDirectories).Any();
    }

    private static Guid CreateDeterministicGuid(string seed)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(bytes);
    }

    private static string InferResourceType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".mp4" or ".mov" or ".webm" or ".avi" or ".mkv" => "video",
            _ => "image"
        };
    }

    private static string InferContentType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            _ => "application/octet-stream"
        };
    }

    private sealed record FeedSeedUserPlan(
        string Username,
        string Email,
        string FullName,
        string ProfileKind,
        bool HasMedia);

    private sealed record MediaFileDefinition(
        string FullPath,
        string RelativePath,
        string ResourceType,
        string ContentType,
        long SizeBytes);

    private sealed record ExpectedFeedSeedResource(
        Guid Id,
        Guid UserId,
        MediaFileDefinition MediaFile);

    public sealed class FeedSeedState
    {
        public DateTime SeededAtUtc { get; set; }

        public List<FeedSeedUserState> Users { get; set; } = [];

        public List<FeedSeedResourceState> Resources { get; set; } = [];
    }

    public sealed class FeedSeedUserState
    {
        public Guid Id { get; set; }

        public string Username { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string? FullName { get; set; }

        public string ProfileKind { get; set; } = string.Empty;

        public bool HasMedia { get; set; }
    }

    public sealed class FeedSeedResourceState
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public string FileName { get; set; } = string.Empty;

        public string RelativePath { get; set; } = string.Empty;

        public string ResourceType { get; set; } = string.Empty;

        public string ContentType { get; set; } = string.Empty;

        public string Link { get; set; } = string.Empty;
    }
}
