using System.Text.Json;
using Application.Abstractions.Workspaces;
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
    private const string RuntimeDirectoryName = "runtime";
    private const string StateFileName = "state.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly MyDbContext _dbContext;
    private readonly IUserWorkspaceService _userWorkspaceService;
    private readonly SampleSeedOptions _options;
    private readonly ILogger<SampleDataSeeder> _logger;

    public SampleDataSeeder(
        MyDbContext dbContext,
        IUserWorkspaceService userWorkspaceService,
        IOptions<SampleSeedOptions> options,
        ILogger<SampleDataSeeder> logger)
    {
        _dbContext = dbContext;
        _userWorkspaceService = userWorkspaceService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("AI sample data seed skipped: SampleSeed:Enabled is false.");
            return;
        }

        var dataRoot = ResolveDataRoot(_options.DataRoot);
        var manifestPath = Path.Combine(dataRoot, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            _logger.LogWarning("AI sample data seed skipped: manifest file was not found at {ManifestPath}.", manifestPath);
            return;
        }

        var manifest = await LoadManifestAsync(manifestPath, cancellationToken);
        if (manifest is null)
        {
            _logger.LogWarning("AI sample data seed skipped: manifest file could not be deserialized.");
            return;
        }

        var statePath = Path.Combine(dataRoot, RuntimeDirectoryName, StateFileName);
        var state = await WaitForStateAsync(statePath, manifest.Workspace.Id, cancellationToken);
        if (state is null)
        {
            _logger.LogWarning("AI sample data seed skipped: sample user state was not ready.");
            return;
        }

        var now = DateTime.UtcNow;
        var existingSession = await _dbContext.ChatSessions
            .FirstOrDefaultAsync(session => session.Id == manifest.ChatSession.Id, cancellationToken);

        if (existingSession is null)
        {
            existingSession = new ChatSession
            {
                Id = manifest.ChatSession.Id,
                CreatedAt = now.AddMinutes(-manifest.Chats.Count - 1)
            };

            _dbContext.ChatSessions.Add(existingSession);
        }

        existingSession.UserId = state.UserId;
        existingSession.WorkspaceId = manifest.Workspace.Id;
        existingSession.SessionName = manifest.ChatSession.Name.Trim();
        existingSession.DeletedAt = null;
        existingSession.UpdatedAt = now;

        for (var index = 0; index < manifest.Chats.Count; index++)
        {
            var chatItem = manifest.Chats[index];
            var existingChat = await _dbContext.Chats
                .FirstOrDefaultAsync(chat => chat.Id == chatItem.Id, cancellationToken);

            if (existingChat is null)
            {
                existingChat = new Chat
                {
                    Id = chatItem.Id,
                    CreatedAt = now.AddMinutes(-manifest.Chats.Count + index)
                };

                _dbContext.Chats.Add(existingChat);
            }

            existingChat.SessionId = manifest.ChatSession.Id;
            existingChat.Prompt = chatItem.Prompt.Trim();
            existingChat.ReferenceResourceIds = JsonSerializer.Serialize(
                chatItem.ResourceIds.Select(item => item.ToString()),
                SerializerOptions);
            existingChat.ResultResourceIds = chatItem.ResultResourceIds.Count == 0
                ? null
                : JsonSerializer.Serialize(
                    chatItem.ResultResourceIds.Select(item => item.ToString()),
                    SerializerOptions);
            existingChat.Config = null;
            existingChat.DeletedAt = null;
            existingChat.UpdatedAt = now;
        }

        await UpsertPostsAsync(manifest, state, now, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Seeded AI sample chat session {SessionId} with {ChatCount} chats and {PostCount} posts for user {UserId}.",
            manifest.ChatSession.Id,
            manifest.Chats.Count,
            manifest.Posts.Count,
            state.UserId);
    }

    private async Task UpsertPostsAsync(
        SampleSeedManifest manifest,
        SampleSeedState state,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (manifest.Posts.Count == 0)
        {
            return;
        }

        var postIds = manifest.Posts
            .Select(item => item.Id)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var existingPosts = await _dbContext.Posts
            .Where(post => postIds.Contains(post.Id))
            .ToDictionaryAsync(post => post.Id, cancellationToken);

        var publicationIds = manifest.Posts
            .SelectMany(item => item.Publications)
            .Select(item => item.Id)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var existingPublications = publicationIds.Count == 0
            ? new Dictionary<Guid, PostPublication>()
            : await _dbContext.PostPublications
                .Where(publication => publicationIds.Contains(publication.Id))
                .ToDictionaryAsync(publication => publication.Id, cancellationToken);

        for (var index = 0; index < manifest.Posts.Count; index++)
        {
            var postItem = manifest.Posts[index];
            var createdAt = postItem.CreatedMinutesAgo.HasValue
                ? now.AddMinutes(-postItem.CreatedMinutesAgo.Value)
                : now.AddMinutes(-(manifest.Posts.Count - index + 10));
            var updatedAt = postItem.UpdatedMinutesAgo.HasValue
                ? now.AddMinutes(-postItem.UpdatedMinutesAgo.Value)
                : createdAt;

            if (!existingPosts.TryGetValue(postItem.Id, out var existingPost))
            {
                existingPost = new Post
                {
                    Id = postItem.Id
                };

                _dbContext.Posts.Add(existingPost);
                existingPosts[postItem.Id] = existingPost;
            }

            existingPost.UserId = state.UserId;
            existingPost.WorkspaceId = manifest.Workspace.Id;
            existingPost.ChatSessionId = postItem.AttachToChatSession ? manifest.ChatSession.Id : null;
            existingPost.SocialMediaId = NormalizeGuid(postItem.SocialMediaId);
            existingPost.Platform = postItem.Platform;
            existingPost.Title = postItem.Title;
            existingPost.Content = postItem.Content;
            existingPost.Status = postItem.Status;
            existingPost.PostBuilderId = null;
            existingPost.CreatedAt = createdAt;
            existingPost.UpdatedAt = updatedAt;
            existingPost.DeletedAt = null;

            if (postItem.Schedule is not null)
            {
                existingPost.ScheduleGroupId = postItem.Schedule.GroupId;
                existingPost.ScheduledAtUtc = now.AddMinutes(postItem.Schedule.ScheduledMinutesFromNow);
                existingPost.ScheduleTimezone = postItem.Schedule.Timezone;
                existingPost.ScheduledSocialMediaIds = postItem.Schedule.SocialMediaIds
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToArray();
                existingPost.ScheduledIsPrivate = postItem.Schedule.IsPrivate;
            }
            else
            {
                existingPost.ScheduleGroupId = null;
                existingPost.ScheduledAtUtc = null;
                existingPost.ScheduleTimezone = null;
                existingPost.ScheduledSocialMediaIds = Array.Empty<Guid>();
                existingPost.ScheduledIsPrivate = null;
            }

            for (var publicationIndex = 0; publicationIndex < postItem.Publications.Count; publicationIndex++)
            {
                var publicationItem = postItem.Publications[publicationIndex];
                var publicationCreatedAt = publicationItem.CreatedMinutesAgo.HasValue
                    ? now.AddMinutes(-publicationItem.CreatedMinutesAgo.Value)
                    : createdAt.AddMinutes(publicationIndex + 1);
                var publicationUpdatedAt = publicationItem.UpdatedMinutesAgo.HasValue
                    ? now.AddMinutes(-publicationItem.UpdatedMinutesAgo.Value)
                    : publicationCreatedAt;

                if (!existingPublications.TryGetValue(publicationItem.Id, out var existingPublication))
                {
                    existingPublication = new PostPublication
                    {
                        Id = publicationItem.Id
                    };

                    _dbContext.PostPublications.Add(existingPublication);
                    existingPublications[publicationItem.Id] = existingPublication;
                }

                existingPublication.PostId = existingPost.Id;
                existingPublication.WorkspaceId = manifest.Workspace.Id;
                existingPublication.SocialMediaId = publicationItem.SocialMediaId;
                existingPublication.SocialMediaType = publicationItem.SocialMediaType;
                existingPublication.DestinationOwnerId = publicationItem.DestinationOwnerId;
                existingPublication.ExternalContentId = publicationItem.ExternalContentId;
                existingPublication.ExternalContentIdType = publicationItem.ExternalContentIdType;
                existingPublication.ContentType = publicationItem.ContentType;
                existingPublication.PublishStatus = publicationItem.PublishStatus;
                existingPublication.PublishedAt = publicationItem.PublishedMinutesAgo.HasValue
                    ? now.AddMinutes(-publicationItem.PublishedMinutesAgo.Value)
                    : null;
                existingPublication.LastMetricsSyncAt = publicationItem.LastMetricsSyncMinutesAgo.HasValue
                    ? now.AddMinutes(-publicationItem.LastMetricsSyncMinutesAgo.Value)
                    : null;
                existingPublication.CreatedAt = publicationCreatedAt;
                existingPublication.UpdatedAt = publicationUpdatedAt;
                existingPublication.DeletedAt = null;
            }
        }
    }

    private async Task<SampleSeedState?> WaitForStateAsync(
        string statePath,
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 180; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(statePath))
            {
                try
                {
                    await using var stream = File.OpenRead(statePath);
                    var state = await JsonSerializer.DeserializeAsync<SampleSeedState>(
                        stream,
                        SerializerOptions,
                        cancellationToken);

                    if (state is not null)
                    {
                        var workspaceResult = await _userWorkspaceService.GetWorkspaceAsync(
                            state.UserId,
                            workspaceId,
                            cancellationToken);

                        if (workspaceResult.IsSuccess && workspaceResult.Value is not null)
                        {
                            return state;
                        }
                    }
                }
                catch (IOException)
                {
                }
                catch (JsonException)
                {
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return null;
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

    public sealed class SampleSeedManifest
    {
        public required SampleSeedWorkspace Workspace { get; set; }

        public required SampleSeedChatSession ChatSession { get; set; }

        public List<SampleSeedResource> Resources { get; set; } = [];

        public List<SampleSeedChat> Chats { get; set; } = [];

        public List<SampleSeedPost> Posts { get; set; } = [];
    }

    public sealed class SampleSeedWorkspace
    {
        public Guid Id { get; set; }
    }

    public sealed class SampleSeedChatSession
    {
        public Guid Id { get; set; }

        public required string Name { get; set; }
    }

    public sealed class SampleSeedResource
    {
        public Guid Id { get; set; }
    }

    public sealed class SampleSeedChat
    {
        public Guid Id { get; set; }

        public required string Prompt { get; set; }

        public List<Guid> ResourceIds { get; set; } = [];

        public List<Guid> ResultResourceIds { get; set; } = [];
    }

    public sealed class SampleSeedPost
    {
        public Guid Id { get; set; }

        public string? Platform { get; set; }

        public Guid? SocialMediaId { get; set; }

        public string? Title { get; set; }

        public PostContent? Content { get; set; }

        public string? Status { get; set; }

        public bool AttachToChatSession { get; set; } = true;

        public int? CreatedMinutesAgo { get; set; }

        public int? UpdatedMinutesAgo { get; set; }

        public SampleSeedPostSchedule? Schedule { get; set; }

        public List<SampleSeedPostPublication> Publications { get; set; } = [];
    }

    public sealed class SampleSeedPostSchedule
    {
        public Guid GroupId { get; set; }

        public int ScheduledMinutesFromNow { get; set; }

        public string? Timezone { get; set; }

        public List<Guid> SocialMediaIds { get; set; } = [];

        public bool? IsPrivate { get; set; }
    }

    public sealed class SampleSeedPostPublication
    {
        public Guid Id { get; set; }

        public Guid SocialMediaId { get; set; }

        public required string SocialMediaType { get; set; }

        public required string DestinationOwnerId { get; set; }

        public required string ExternalContentId { get; set; }

        public required string ExternalContentIdType { get; set; }

        public required string ContentType { get; set; }

        public required string PublishStatus { get; set; }

        public int? PublishedMinutesAgo { get; set; }

        public int? LastMetricsSyncMinutesAgo { get; set; }

        public int? CreatedMinutesAgo { get; set; }

        public int? UpdatedMinutesAgo { get; set; }
    }

    public sealed class SampleSeedState
    {
        public Guid UserId { get; set; }

        public Guid WorkspaceId { get; set; }

        public List<Guid> ResourceIds { get; set; } = [];

        public DateTime SeededAtUtc { get; set; }
    }

    private static Guid? NormalizeGuid(Guid? value)
    {
        return value == Guid.Empty ? null : value;
    }
}
