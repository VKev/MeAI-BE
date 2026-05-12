using Application.PublishingSchedules;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using SharedLibrary.Extensions;
using System.Data;

namespace Infrastructure.Repositories;

public sealed class PublishingScheduleRepository : IPublishingScheduleRepository
{
    private readonly MyDbContext _dbContext;
    private readonly DbSet<PublishingSchedule> _dbSet;

    public PublishingScheduleRepository(MyDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<PublishingSchedule>();
    }

    public Task AddAsync(PublishingSchedule entity, CancellationToken cancellationToken)
    {
        return _dbSet.AddAsync(entity, cancellationToken).AsTask();
    }

    public void Update(PublishingSchedule entity)
    {
        _dbSet.Update(entity);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<PublishingSchedule?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return BaseQuery(_dbSet.AsNoTracking())
            .FirstOrDefaultAsync(schedule => schedule.Id == id, cancellationToken);
    }

    public Task<PublishingSchedule?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        return BaseQuery(_dbSet)
            .FirstOrDefaultAsync(schedule => schedule.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<PublishingSchedule>> GetByUserIdAsync(
        Guid userId,
        Guid? workspaceId,
        string? status,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = BaseQuery(_dbSet.AsNoTracking())
            .Where(schedule => schedule.UserId == userId && schedule.DeletedAt == null);

        if (workspaceId.HasValue)
        {
            query = query.Where(schedule => schedule.WorkspaceId == workspaceId.Value);
        }

        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedStatus))
        {
            query = query.Where(schedule => schedule.Status == normalizedStatus);
        }

        return await query
            .OrderByDescending(schedule => schedule.CreatedAt)
            .ThenByDescending(schedule => schedule.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ClaimDueAgenticSchedulesAsync(
        DateTime dueBeforeUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return Array.Empty<Guid>();
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
                    SELECT id
                    FROM publishing_schedules
                    WHERE deleted_at IS NULL
                      AND mode = @mode
                      AND status = @waiting_status
                      AND execute_at_utc IS NOT NULL
                      AND execute_at_utc <= @due_before_utc
                    ORDER BY execute_at_utc, id
                    FOR UPDATE SKIP LOCKED
                    LIMIT @limit
                )
                UPDATE publishing_schedules AS schedule
                SET status = @executing_status,
                    last_execution_at = @updated_at,
                    updated_at = @updated_at,
                    error_code = NULL,
                    error_message = NULL,
                    next_retry_at = NULL
                FROM due
                WHERE schedule.id = due.id
                RETURNING schedule.id;
                """;

            command.Parameters.Add(new NpgsqlParameter("mode", NpgsqlDbType.Text)
            {
                Value = PublishingScheduleState.AgenticMode
            });
            command.Parameters.Add(new NpgsqlParameter("waiting_status", NpgsqlDbType.Text)
            {
                Value = PublishingScheduleState.StatusWaitingForExecution
            });
            command.Parameters.Add(new NpgsqlParameter("executing_status", NpgsqlDbType.Text)
            {
                Value = PublishingScheduleState.StatusExecuting
            });
            command.Parameters.Add(new NpgsqlParameter("due_before_utc", NpgsqlDbType.TimestampTz)
            {
                Value = dueBeforeUtc
            });
            command.Parameters.Add(new NpgsqlParameter("updated_at", NpgsqlDbType.TimestampTz)
            {
                Value = DateTimeExtensions.PostgreSqlUtcNow
            });
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer)
            {
                Value = limit
            });

            var claimedIds = new List<Guid>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                claimedIds.Add(reader.GetGuid(0));
            }

            return claimedIds;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static IQueryable<PublishingSchedule> BaseQuery(IQueryable<PublishingSchedule> query)
    {
        return query
            .Include(schedule => schedule.Items)
            .Include(schedule => schedule.Targets);
    }
}
