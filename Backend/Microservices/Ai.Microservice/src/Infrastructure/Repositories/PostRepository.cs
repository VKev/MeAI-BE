using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using SharedLibrary.Extensions;
using System.Data;

namespace Infrastructure.Repositories;

public sealed class PostRepository : IPostRepository
{
    private const string ScheduledStatus = "scheduled";
    private const string ProcessingStatus = "processing";
    private const string FailedStatus = "failed";

    private readonly MyDbContext _dbContext;
    private readonly DbSet<Post> _dbSet;

    public PostRepository(MyDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<Post>();
    }

    public Task AddAsync(Post entity, CancellationToken cancellationToken)
    {
        return _dbSet.AddAsync(entity, cancellationToken).AsTask();
    }

    public void Update(Post entity)
    {
        _dbSet.Update(entity);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Post?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbSet.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Post?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbSet.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Post>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<Post>();
        }

        return await _dbSet.AsNoTracking()
            .Where(post => ids.Contains(post.Id) && post.DeletedAt == null)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Post>> GetByIdsForUpdateAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await _dbSet
            .Where(post => ids.Contains(post.Id) && post.DeletedAt == null)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Post>> GetByUserIdAsync(
        Guid userId,
        DateTime? cursorCreatedAt,
        Guid? cursorId,
        int limit,
        string? status,
        Guid? socialMediaId,
        string? platform,
        CancellationToken cancellationToken)
    {
        var query = _dbSet.AsNoTracking()
            .Where(p => p.UserId == userId && p.DeletedAt == null);

        query = ApplyFilters(query, status, socialMediaId, platform);

        if (cursorCreatedAt.HasValue && cursorId.HasValue)
        {
            var createdAt = cursorCreatedAt.Value;
            var lastId = cursorId.Value;
            query = query.Where(post =>
                (post.CreatedAt < createdAt) ||
                (post.CreatedAt == createdAt && post.Id.CompareTo(lastId) < 0));
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Post>> GetTrackedByPostBuilderIdAsync(
        Guid postBuilderId,
        CancellationToken cancellationToken)
    {
        return await _dbSet
            .Where(p => p.PostBuilderId == postBuilderId && p.DeletedAt == null)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Post>> GetByUserIdAndWorkspaceIdAsync(
        Guid userId,
        Guid workspaceId,
        DateTime? cursorCreatedAt,
        Guid? cursorId,
        int limit,
        string? status,
        Guid? socialMediaId,
        string? platform,
        CancellationToken cancellationToken)
    {
        var query = _dbSet.AsNoTracking()
            .Where(p => p.UserId == userId &&
                        p.WorkspaceId == workspaceId &&
                        p.DeletedAt == null);

        query = ApplyFilters(query, status, socialMediaId, platform);

        if (cursorCreatedAt.HasValue && cursorId.HasValue)
        {
            var createdAt = cursorCreatedAt.Value;
            var lastId = cursorId.Value;
            query = query.Where(post =>
                (post.CreatedAt < createdAt) ||
                (post.CreatedAt == createdAt && post.Id.CompareTo(lastId) < 0));
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Post>> GetByUserIdAndChatSessionIdAsync(
        Guid userId,
        Guid chatSessionId,
        DateTime? cursorCreatedAt,
        Guid? cursorId,
        int limit,
        string? status,
        Guid? socialMediaId,
        string? platform,
        CancellationToken cancellationToken)
    {
        var query = _dbSet.AsNoTracking()
            .Where(p => p.UserId == userId &&
                        p.ChatSessionId == chatSessionId &&
                        p.DeletedAt == null);

        query = ApplyFilters(query, status, socialMediaId, platform);

        if (cursorCreatedAt.HasValue && cursorId.HasValue)
        {
            var createdAt = cursorCreatedAt.Value;
            var lastId = cursorId.Value;
            query = query.Where(post =>
                (post.CreatedAt < createdAt) ||
                (post.CreatedAt == createdAt && post.Id.CompareTo(lastId) < 0));
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScheduledPostDispatchCandidate>> ClaimDueScheduledPostsAsync(
        DateTime dueBeforeUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return Array.Empty<ScheduledPostDispatchCandidate>();
        }

        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                WITH due AS (
                    SELECT
                        id,
                        user_id,
                        scheduled_social_media_ids,
                        scheduled_is_private,
                        schedule_group_id
                    FROM posts
                    WHERE deleted_at IS NULL
                      AND workspace_id IS NOT NULL
                      AND status = @scheduled_status
                      AND schedule_group_id IS NOT NULL
                      AND scheduled_at_utc IS NOT NULL
                      AND scheduled_at_utc <= @due_before_utc
                      AND scheduled_social_media_ids IS NOT NULL
                      AND cardinality(scheduled_social_media_ids) > 0
                    ORDER BY scheduled_at_utc, id
                    FOR UPDATE SKIP LOCKED
                    LIMIT @limit
                )
                UPDATE posts AS post
                SET status = @processing_status,
                    updated_at = @updated_at,
                    schedule_group_id = NULL,
                    scheduled_social_media_ids = ARRAY[]::uuid[],
                    scheduled_is_private = NULL,
                    schedule_timezone = NULL,
                    scheduled_at_utc = NULL
                FROM due
                WHERE post.id = due.id
                RETURNING
                    due.id,
                    due.user_id,
                    due.scheduled_social_media_ids,
                    due.scheduled_is_private,
                    due.schedule_group_id;
                """;

            command.Parameters.Add(new NpgsqlParameter("scheduled_status", NpgsqlDbType.Text) { Value = ScheduledStatus });
            command.Parameters.Add(new NpgsqlParameter("processing_status", NpgsqlDbType.Text) { Value = ProcessingStatus });
            command.Parameters.Add(new NpgsqlParameter("due_before_utc", NpgsqlDbType.TimestampTz) { Value = dueBeforeUtc });
            command.Parameters.Add(new NpgsqlParameter("updated_at", NpgsqlDbType.TimestampTz) { Value = DateTimeExtensions.PostgreSqlUtcNow });
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = limit });

            var claimed = new List<ScheduledPostDispatchCandidate>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                claimed.Add(new ScheduledPostDispatchCandidate(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetFieldValue<Guid[]>(2),
                    reader.IsDBNull(3) ? null : reader.GetBoolean(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4)));
            }

            return claimed;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    public Task MarkScheduledDispatchFailedAsync(Guid postId, CancellationToken cancellationToken)
    {
        var now = DateTimeExtensions.PostgreSqlUtcNow;
        return _dbSet
            .Where(post => post.Id == postId && post.DeletedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(post => post.Status, FailedStatus)
                .SetProperty(post => post.UpdatedAt, now), cancellationToken);
    }

    private static IQueryable<Post> ApplyFilters(
        IQueryable<Post> query,
        string? status,
        Guid? socialMediaId,
        string? platform)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(post => post.Status == status);
        }

        if (socialMediaId.HasValue && socialMediaId.Value != Guid.Empty)
        {
            query = query.Where(post => post.SocialMediaId == socialMediaId.Value);
        }

        var platformAliases = GetPlatformAliases(platform);
        if (platformAliases.Length > 0)
        {
            query = query.Where(post =>
                post.Platform != null &&
                platformAliases.Contains(post.Platform.Trim().ToLower()));
        }

        return query;
    }

    private static string[] GetPlatformAliases(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return Array.Empty<string>();
        }

        var normalized = platform.Trim().ToLowerInvariant();
        return normalized switch
        {
            "facebook" or "fb" => ["facebook", "fb"],
            "instagram" or "ig" => ["instagram", "ig"],
            "threads" or "thread" => ["threads", "thread"],
            "tiktok" => ["tiktok"],
            _ => [normalized]
        };
    }
}
