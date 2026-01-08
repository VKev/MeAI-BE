using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class VideoTaskConfiguration : IEntityTypeConfiguration<VideoTask>
{
    public void Configure(EntityTypeBuilder<VideoTask> entity)
    {
        entity.HasKey(e => e.Id).HasName("video_tasks_pkey");

        entity.ToTable("video_tasks");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.CorrelationId).HasColumnName("correlation_id");
        entity.Property(e => e.VeoTaskId).HasColumnName("veo_task_id");
        entity.Property(e => e.Prompt).HasColumnName("prompt");
        entity.Property(e => e.Model).HasColumnName("model");
        entity.Property(e => e.AspectRatio).HasColumnName("aspect_ratio");
        entity.Property(e => e.Status).HasColumnName("status");
        entity.Property(e => e.ResultUrls).HasColumnName("result_urls").HasColumnType("jsonb");
        entity.Property(e => e.Resolution).HasColumnName("resolution");
        entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
        entity.Property(e => e.ErrorCode).HasColumnName("error_code");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UserId).HasColumnName("user_id");

        entity.HasIndex(e => e.CorrelationId).HasDatabaseName("ix_video_tasks_correlation_id");
        entity.HasIndex(e => e.UserId).HasDatabaseName("ix_video_tasks_user_id");
    }
}
