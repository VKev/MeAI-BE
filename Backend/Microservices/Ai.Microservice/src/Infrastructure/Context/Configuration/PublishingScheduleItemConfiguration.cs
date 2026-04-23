using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class PublishingScheduleItemConfiguration : IEntityTypeConfiguration<PublishingScheduleItem>
{
    public void Configure(EntityTypeBuilder<PublishingScheduleItem> entity)
    {
        entity.HasKey(e => e.Id).HasName("publishing_schedule_items_pkey");

        entity.ToTable("publishing_schedule_items");

        entity.HasIndex(e => new { e.ScheduleId, e.SortOrder }, "ix_publishing_schedule_items_schedule_sort_order");
        entity.HasIndex(e => new { e.ScheduleId, e.ItemId }, "ix_publishing_schedule_items_schedule_item_id");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.ScheduleId).HasColumnName("schedule_id");
        entity.Property(e => e.ItemType).HasColumnName("item_type");
        entity.Property(e => e.ItemId).HasColumnName("item_id");
        entity.Property(e => e.SortOrder).HasColumnName("sort_order");
        entity.Property(e => e.ExecutionBehavior).HasColumnName("execution_behavior");
        entity.Property(e => e.Status).HasColumnName("status");
        entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
        entity.Property(e => e.LastExecutionAt).HasColumnName("last_execution_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
    }
}
