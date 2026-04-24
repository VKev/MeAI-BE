using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Domain.Entities;
using Infrastructure.Configuration;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedLibrary.Authentication;

namespace Infrastructure.Logic.Seeding;

public sealed class FeedDemoUserSeeder
{
    private const string RuntimeDirectoryName = "runtime";
    private const string StateFileName = "users.state.json";
    private const string MediaDirectoryName = "media";
    private const string DefaultPassword = "12345678";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly MyDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly FeedSeedOptions _options;
    private readonly DefaultUserSeedOptions _defaultUserOptions;
    private readonly AdminSeedOptions _adminSeedOptions;
    private readonly ILogger<FeedDemoUserSeeder> _logger;

    public FeedDemoUserSeeder(
        MyDbContext dbContext,
        IPasswordHasher passwordHasher,
        IOptions<FeedSeedOptions> options,
        IOptions<DefaultUserSeedOptions> defaultUserOptions,
        IOptions<AdminSeedOptions> adminSeedOptions,
        ILogger<FeedDemoUserSeeder> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
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

        if (!_options.Enabled)
        {
            _logger.LogInformation("Feed demo user seed skipped: FeedSeed:Enabled is false.");
            return;
        }

        var mediaRoot = Path.Combine(dataRoot, MediaDirectoryName);
        if (!Directory.Exists(mediaRoot))
        {
            _logger.LogWarning("Feed demo user seed skipped: media directory was not found at {MediaRoot}.", mediaRoot);
            return;
        }

        var mediaFiles = Directory
            .EnumerateFiles(mediaRoot, "*", SearchOption.AllDirectories)
            .Select(filePath => new MediaFileDefinition(
                RelativePath: Path.GetRelativePath(mediaRoot, filePath).Replace('\\', '/'),
                ResourceType: InferResourceType(filePath),
                ContentType: InferContentType(filePath),
                FileSizeBytes: new FileInfo(filePath).Length))
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
                    "Feed demo user seed detected existing dataset and refreshed state at {StatePath}.",
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
        foreach (var user in users.Where(item => mediaRichUsernames.Contains(item.Username)))
        {
            foreach (var mediaFile in mediaFiles)
            {
                var resourceId = CreateDeterministicGuid($"feed-seed:resource:{user.Username}:{mediaFile.RelativePath}");
                var link = BuildSeedMediaUrl(_options.PublicBaseUrl, mediaFile.RelativePath);
                var createdAt = now.AddMinutes(-(resources.Count + 1));

                resources.Add(new Resource
                {
                    Id = resourceId,
                    UserId = user.Id,
                    Link = link,
                    Status = "ready",
                    ResourceType = mediaFile.ResourceType,
                    ContentType = mediaFile.ContentType,
                    FileSizeBytes = mediaFile.FileSizeBytes,
                    CreatedAt = createdAt,
                    UpdatedAt = createdAt,
                    IsDeleted = false,
                    DeletedAt = null
                });

                stateResources.Add(new FeedSeedResourceState
                {
                    Id = resourceId,
                    UserId = user.Id,
                    FileName = Path.GetFileName(mediaFile.RelativePath),
                    RelativePath = mediaFile.RelativePath,
                    ResourceType = mediaFile.ResourceType,
                    ContentType = mediaFile.ContentType,
                    FileSizeBytes = mediaFile.FileSizeBytes,
                    Link = link
                });
            }
        }

        _dbContext.Resources.AddRange(resources);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var state = new FeedSeedState
        {
            SeededAtUtc = now,
            Users = userPlans.Select(plan => new FeedSeedUserState
            {
                Id = CreateDeterministicGuid($"feed-seed:user:{plan.Username}"),
                Username = plan.Username,
                Email = plan.Email,
                FullName = plan.FullName,
                ProfileKind = plan.ProfileKind,
                HasMedia = plan.HasMedia
            }).ToList(),
            Resources = stateResources
        };

        var json = JsonSerializer.Serialize(state, SerializerOptions);
        await File.WriteAllTextAsync(statePath, json, cancellationToken);

        _logger.LogInformation(
            "Seeded {UserCount} feed demo users and {ResourceCount} media resources. State written to {StatePath}.",
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

        var mediaRichUsers = expectedUsers
            .Where(item => item.Plan.HasMedia)
            .ToList();

        var expectedResources = mediaRichUsers
            .SelectMany(
                item => mediaFiles.Select(mediaFile => new FeedSeedResourceState
                {
                    Id = CreateDeterministicGuid($"feed-seed:resource:{item.Plan.Username}:{mediaFile.RelativePath}"),
                    UserId = item.UserId,
                    FileName = Path.GetFileName(mediaFile.RelativePath),
                    RelativePath = mediaFile.RelativePath,
                    ResourceType = mediaFile.ResourceType,
                    ContentType = mediaFile.ContentType,
                    FileSizeBytes = mediaFile.FileSizeBytes,
                    Link = BuildSeedMediaUrl(_options.PublicBaseUrl, mediaFile.RelativePath)
                }))
            .ToList();

        var expectedResourceIds = expectedResources.Select(item => item.Id).ToList();
        var existingResourceIds = await _dbContext.Resources
            .AsNoTracking()
            .Where(resource => !resource.IsDeleted && expectedResourceIds.Contains(resource.Id))
            .Select(resource => resource.Id)
            .ToListAsync(cancellationToken);

        if (existingResourceIds.Count != expectedResourceIds.Count)
        {
            return false;
        }

        var state = new FeedSeedState
        {
            SeededAtUtc = DateTime.UtcNow,
            Users = userPlans.Select(plan => new FeedSeedUserState
            {
                Id = CreateDeterministicGuid($"feed-seed:user:{plan.Username}"),
                Username = plan.Username,
                Email = plan.Email,
                FullName = plan.FullName,
                ProfileKind = plan.ProfileKind,
                HasMedia = plan.HasMedia
            }).ToList(),
            Resources = expectedResources
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

    private static string BuildSeedMediaUrl(string publicBaseUrl, string relativePath)
    {
        var normalizedBaseUrl = string.IsNullOrWhiteSpace(publicBaseUrl)
            ? "http://localhost:2406"
            : publicBaseUrl.TrimEnd('/');

        var encodedPath = string.Join(
            "/",
            relativePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));

        return $"{normalizedBaseUrl}/api/User/seed-media/{encodedPath}";
    }

    private sealed record FeedSeedUserPlan(
        string Username,
        string Email,
        string FullName,
        string ProfileKind,
        bool HasMedia);

    private sealed record MediaFileDefinition(
        string RelativePath,
        string ResourceType,
        string ContentType,
        long FileSizeBytes);

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

        public long FileSizeBytes { get; set; }

        public string Link { get; set; } = string.Empty;
    }
}
