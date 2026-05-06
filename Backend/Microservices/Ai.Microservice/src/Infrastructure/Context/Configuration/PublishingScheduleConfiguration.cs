using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class PublishingScheduleConfiguration : IEntityTypeConfiguration<PublishingSchedule>
{
    public void Configure(EntityTypeBuilder<PublishingSchedule> entity)
    {
        entity.HasKey(e => e.Id).HasName("publishing_schedules_pkey");

        entity.ToTable("publishing_schedules");

        entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "ix_publishing_schedules_user_created_at")
            .IsDescending(false, true);
        entity.HasIndex(e => new { e.Status, e.ExecuteAtUtc }, "ix_publishing_schedules_status_execute_at_utc");
        entity.HasIndex(e => e.WorkspaceId, "ix_publishing_schedules_workspace_id");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id");
        entity.Property(e => e.Name).HasColumnName("name");
        entity.Property(e => e.Mode).HasColumnName("mode");
        entity.Property(e => e.Status).HasColumnName("status");
        entity.Property(e => e.Timezone).HasColumnName("timezone");
        entity.Property(e => e.ExecuteAtUtc).HasColumnName("execute_at_utc").HasColumnType("timestamp with time zone");
        entity.Property(e => e.IsPrivate).HasColumnName("is_private");
        entity.Property(e => e.CreatedBy).HasColumnName("created_by");
        entity.Property(e => e.PlatformPreference).HasColumnName("platform_preference");
        entity.Property(e => e.AgentPrompt).HasColumnName("agent_prompt");
        entity.Property(e => e.MaxContentLength).HasColumnName("max_content_length");
        entity.Property(e => e.SearchQueryTemplate).HasColumnName("search_query_template");
        entity.Property(e => e.ExecutionContextJson).HasColumnName("execution_context_json").HasColumnType("jsonb");
        entity.Property(e => e.LastExecutionAt).HasColumnName("last_execution_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.NextRetryAt).HasColumnName("next_retry_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.ErrorCode).HasColumnName("error_code");
        entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");

        entity.HasMany(e => e.Items)
            .WithOne(item => item.Schedule)
            .HasForeignKey(item => item.ScheduleId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("publishing_schedule_items_schedule_id_fkey");

        entity.HasMany(e => e.Targets)
            .WithOne(target => target.Schedule)
            .HasForeignKey(target => target.ScheduleId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("publishing_schedule_targets_schedule_id_fkey");
    }
}
