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

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Seeded AI sample chat session {SessionId} with {ChatCount} chats for user {UserId}.",
            manifest.ChatSession.Id,
            manifest.Chats.Count,
            state.UserId);
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

    public sealed class SampleSeedState
    {
        public Guid UserId { get; set; }

        public Guid WorkspaceId { get; set; }

        public List<Guid> ResourceIds { get; set; } = [];

        public DateTime SeededAtUtc { get; set; }
    }
}
