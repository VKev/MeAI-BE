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
    private const string ExternalContentIdType = "post_id";
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
        return await _dbSet
            .AsNoTracking()
            .Include(post => post.PostBuilder)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Post?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbSet
            .Include(post => post.PostBuilder)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
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

    public async Task<List<Post>> GetByUserIdAndSocialMediaIdForUpdateAsync(
        Guid userId,
        Guid socialMediaId,
        CancellationToken cancellationToken)
    {
        return await _dbSet
            .Where(post =>
                post.UserId == userId &&
                post.SocialMediaId == socialMediaId &&
                post.DeletedAt == null)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> AttachSocialMediaPostsToWorkspaceAsync(
        Guid userId,
        Guid socialMediaId,
        Guid workspaceId,
        DateTime updatedAt,
        CancellationToken cancellationToken)
    {
        var publications = await _dbContext.Set<PostPublication>()
            .Where(publication =>
                publication.UserId == userId &&
                publication.SocialMediaId == socialMediaId &&
                !publication.DeletedAt.HasValue)
            .ToListAsync(cancellationToken);

        var publicationPostIds = publications
            .Select(publication => publication.PostId)
            .Distinct()
            .ToList();

        var posts = await _dbSet
            .Where(post =>
                post.UserId == userId &&
                post.DeletedAt == null &&
                (post.SocialMediaId == socialMediaId || publicationPostIds.Contains(post.Id)))
            .ToListAsync(cancellationToken);

        var postIds = posts.Select(post => post.Id).ToHashSet();
        var existingMappings = await _dbContext.Set<SocialMediaPostWorkspace>()
            .Where(mapping =>
                mapping.UserId == userId &&
                mapping.WorkspaceId == workspaceId &&
                mapping.SocialMediaId == socialMediaId &&
                postIds.Contains(mapping.PostId))
            .ToListAsync(cancellationToken);

        var mappingsByPostId = existingMappings
            .GroupBy(mapping => mapping.PostId)
            .ToDictionary(group => group.Key, group => group.First());

        var changedPostIds = new HashSet<Guid>();
        foreach (var post in posts)
        {
            if (mappingsByPostId.TryGetValue(post.Id, out var existingMapping))
            {
                if (existingMapping.DeletedAt.HasValue)
                {
                    existingMapping.DeletedAt = null;
                    existingMapping.UpdatedAt = updatedAt;
                    changedPostIds.Add(post.Id);
                }

                continue;
            }

            await _dbContext.Set<SocialMediaPostWorkspace>().AddAsync(
                new SocialMediaPostWorkspace
                {
                    Id = Guid.CreateVersion7(),
                    UserId = userId,
                    PostId = post.Id,
                    SocialMediaId = socialMediaId,
                    WorkspaceId = workspaceId,
                    CreatedAt = updatedAt
                },
                cancellationToken);
            changedPostIds.Add(post.Id);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return changedPostIds.Count;
    }

    public async Task<int> DetachSocialMediaPostsFromWorkspaceAsync(
        Guid userId,
        Guid socialMediaId,
        Guid workspaceId,
        DateTime updatedAt,
        CancellationToken cancellationToken)
    {
        var mappings = await _dbContext.Set<SocialMediaPostWorkspace>()
            .Where(mapping =>
                mapping.UserId == userId &&
                mapping.SocialMediaId == socialMediaId &&
                mapping.WorkspaceId == workspaceId &&
                !mapping.DeletedAt.HasValue)
            .ToListAsync(cancellationToken);

        var candidatePostIds = mappings
            .Select(mapping => mapping.PostId)
            .Distinct()
            .ToList();

        // Clear the old single-workspace assignment left by deployments before
        // social_media_post_workspaces existed. Native workspace posts without a
        // social-account sync mapping remain untouched.
        var legacyPosts = await _dbSet
            .Where(post =>
                post.UserId == userId &&
                post.SocialMediaId == socialMediaId &&
                post.WorkspaceId == workspaceId &&
                post.DeletedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var postId in legacyPosts.Select(post => post.Id))
        {
            if (!candidatePostIds.Contains(postId))
            {
                candidatePostIds.Add(postId);
            }
        }

        if (candidatePostIds.Count == 0)
        {
            return 0;
        }

        foreach (var mapping in mappings)
        {
            mapping.DeletedAt = updatedAt;
            mapping.UpdatedAt = updatedAt;
        }

        var removedMappingIds = mappings
            .Select(mapping => mapping.Id)
            .ToArray();

        var remainingWorkspaceMappings = await _dbContext.Set<SocialMediaPostWorkspace>()
            .Where(mapping =>
                mapping.UserId == userId &&
                candidatePostIds.Contains(mapping.PostId) &&
                mapping.WorkspaceId == workspaceId &&
                !mapping.DeletedAt.HasValue &&
                !removedMappingIds.Contains(mapping.Id))
            .ToListAsync(cancellationToken);

        var remainingPostIds = remainingWorkspaceMappings
            .Select(mapping => mapping.PostId)
            .ToHashSet();

        var posts = await _dbSet
            .Where(post =>
                candidatePostIds.Contains(post.Id) &&
                post.UserId == userId &&
                post.WorkspaceId == workspaceId &&
                post.DeletedAt == null)
            .ToListAsync(cancellationToken);

        var changedPostIds = new HashSet<Guid>(candidatePostIds);
        foreach (var post in posts)
        {
            if (remainingPostIds.Contains(post.Id))
            {
                continue;
            }

            if (post.SocialMediaId == socialMediaId)
            {
                post.WorkspaceId = null;
                post.UpdatedAt = updatedAt;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return changedPostIds.Count;
    }

    public async Task<int> SoftDeleteMissingSocialMediaPostsAsync(
        Guid userId,
        Guid socialMediaId,
        IReadOnlySet<string> activeExternalContentIds,
        DateTime updatedAt,
        CancellationToken cancellationToken)
    {
        var activeIds = activeExternalContentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var missingPublications = await _dbContext.Set<PostPublication>()
            .Where(publication =>
                publication.SocialMediaId == socialMediaId &&
                publication.ExternalContentIdType == ExternalContentIdType &&
                !publication.DeletedAt.HasValue &&
                !activeIds.Contains(publication.ExternalContentId))
            .ToListAsync(cancellationToken);

        if (missingPublications.Count == 0)
        {
            return 0;
        }

        var missingPublicationIds = missingPublications
            .Select(publication => publication.Id)
            .ToHashSet();

        var postIds = missingPublications
            .Select(publication => publication.PostId)
            .Distinct()
            .ToList();

        foreach (var publication in missingPublications)
        {
            // DeletedAt is enough to hide a missing remote publication. Keep the
            // existing status because the database check constraint has no "deleted" status.
            publication.DeletedAt = updatedAt;
            publication.UpdatedAt = updatedAt;
        }

        var allPostPublications = await _dbContext.Set<PostPublication>()
            .Where(publication => postIds.Contains(publication.PostId))
            .ToListAsync(cancellationToken);

        var remainingByPostId = allPostPublications
            .Where(publication =>
                !publication.DeletedAt.HasValue &&
                !missingPublicationIds.Contains(publication.Id))
            .GroupBy(publication => publication.PostId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(publication => publication.PublishedAt ?? publication.CreatedAt)
                    .ThenByDescending(publication => publication.Id)
                    .First());

        var posts = await _dbSet
            .Where(post =>
                postIds.Contains(post.Id) &&
                post.UserId == userId &&
                post.DeletedAt == null)
            .ToListAsync(cancellationToken);

        var changedPostIds = new HashSet<Guid>(postIds);
        foreach (var post in posts)
        {
            if (remainingByPostId.TryGetValue(post.Id, out var remainingPublication))
            {
                if (post.SocialMediaId == socialMediaId)
                {
                    post.SocialMediaId = remainingPublication.SocialMediaId;
                    post.Platform = remainingPublication.SocialMediaType;
                    post.WorkspaceId = remainingPublication.WorkspaceId == Guid.Empty
                        ? null
                        : remainingPublication.WorkspaceId;
                    post.UpdatedAt = updatedAt;
                }

                continue;
            }

            if (post.SocialMediaId == socialMediaId)
            {
                post.DeletedAt = updatedAt;
                post.UpdatedAt = updatedAt;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return changedPostIds.Count;
    }

    public async Task<IReadOnlyList<Post>> GetActiveByUserIdExcludingIdsAsync(
        Guid userId,
        IReadOnlyList<Guid> excludedPostIds,
        CancellationToken cancellationToken)
    {
        var query = _dbSet
            .AsNoTracking()
            .Where(post => post.UserId == userId && post.DeletedAt == null);

        if (excludedPostIds.Count > 0)
        {
            query = query.Where(post => !excludedPostIds.Contains(post.Id));
        }

        return await query.ToListAsync(cancellationToken);
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
        var failedRecommendationPostIds = FailedRecommendationPostIds(userId);
        var includeFailedRecommendationPosts = IsFailedStatusFilter(status);
        var query = _dbSet.AsNoTracking()
            .Where(p =>
                p.UserId == userId &&
                (p.DeletedAt == null ||
                 (includeFailedRecommendationPosts && failedRecommendationPostIds.Contains(p.Id))));

        query = ExcludeUneditedPostBuilderDrafts(query);
        query = ApplyFilters(query, status, socialMediaId, platform, failedRecommendationPostIds);

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
        var failedRecommendationPostIds = FailedRecommendationPostIds(userId);
        var includeFailedRecommendationPosts = IsFailedStatusFilter(status);
        var query = _dbSet.AsNoTracking()
            .Where(p => p.UserId == userId &&
                        (p.WorkspaceId == workspaceId ||
                         _dbContext.Set<SocialMediaPostWorkspace>().Any(mapping =>
                             mapping.UserId == userId &&
                             mapping.WorkspaceId == workspaceId &&
                             mapping.PostId == p.Id &&
                             !mapping.DeletedAt.HasValue)) &&
                        (p.DeletedAt == null ||
                         (includeFailedRecommendationPosts && failedRecommendationPostIds.Contains(p.Id))));

        query = ExcludeUneditedPostBuilderDrafts(query);
        query = ApplyFilters(query, status, socialMediaId, platform, failedRecommendationPostIds);

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
        var failedRecommendationPostIds = FailedRecommendationPostIds(userId);
        var includeFailedRecommendationPosts = IsFailedStatusFilter(status);
        var query = _dbSet.AsNoTracking()
            .Where(p => p.UserId == userId &&
                        p.ChatSessionId == chatSessionId &&
                        (p.DeletedAt == null ||
                         (includeFailedRecommendationPosts && failedRecommendationPostIds.Contains(p.Id))));

        query = ExcludeUneditedPostBuilderDrafts(query);
        query = ApplyFilters(query, status, socialMediaId, platform, failedRecommendationPostIds);

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

    public void DeleteRange(IEnumerable<Post> entities)
    {
        _dbSet.RemoveRange(entities);
    }

    private IQueryable<Guid> FailedRecommendationPostIds(Guid userId)
    {
        return _dbContext.Set<DraftPostTask>()
            .AsNoTracking()
            .Where(task =>
                task.UserId == userId &&
                task.Status == DraftPostTaskStatuses.Failed &&
                task.ResultPostId.HasValue)
            .Select(task => task.ResultPostId!.Value);
    }

    private static bool IsFailedStatusFilter(string? status)
    {
        return string.Equals(status?.Trim(), FailedStatus, StringComparison.OrdinalIgnoreCase);
    }

    private static IQueryable<Post> ExcludeUneditedPostBuilderDrafts(IQueryable<Post> query)
    {
        return query.Where(post =>
            post.PostBuilderId == null ||
            post.SocialMediaId != null ||
            post.UpdatedAt != null ||
            post.Title != null ||
            (post.Status != null && post.Status.ToLower() != "draft"));
    }

    private static IQueryable<Post> ApplyFilters(
        IQueryable<Post> query,
        string? status,
        Guid? socialMediaId,
        string? platform,
        IQueryable<Guid>? failedRecommendationPostIds = null)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim();
            if (IsFailedStatusFilter(normalizedStatus) && failedRecommendationPostIds is not null)
            {
                query = query.Where(post =>
                    post.Status == FailedStatus ||
                    failedRecommendationPostIds.Contains(post.Id));
            }
            else
            {
                query = query.Where(post => post.Status == normalizedStatus);
            }
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
