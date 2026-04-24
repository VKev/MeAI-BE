using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class PublishingScheduleTargetConfiguration : IEntityTypeConfiguration<PublishingScheduleTarget>
{
    public void Configure(EntityTypeBuilder<PublishingScheduleTarget> entity)
    {
        entity.HasKey(e => e.Id).HasName("publishing_schedule_targets_pkey");

        entity.ToTable("publishing_schedule_targets");

        entity.HasIndex(e => new { e.ScheduleId, e.SocialMediaId }, "ix_publishing_schedule_targets_schedule_social_media");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.ScheduleId).HasColumnName("schedule_id");
        entity.Property(e => e.SocialMediaId).HasColumnName("social_media_id");
        entity.Property(e => e.Platform).HasColumnName("platform");
        entity.Property(e => e.TargetLabel).HasColumnName("target_label");
        entity.Property(e => e.IsPrimary).HasColumnName("is_primary");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
    }
}
