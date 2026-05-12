using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> entity)
    {
        entity.HasKey(e => e.Id).HasName("notifications_pkey");

        entity.ToTable("notifications");

        entity.HasIndex(e => new { e.Type, e.CreatedAt }, "ix_notifications_type_created_at")
            .IsDescending(false, true);

        entity.HasIndex(e => new { e.Source, e.Type, e.CreatedAt }, "ix_notifications_source_type_created_at")
            .IsDescending(false, false, true);

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.Source).HasColumnName("source").HasMaxLength(100).IsRequired();
        entity.Property(e => e.Type).HasColumnName("type").HasMaxLength(100).IsRequired();
        entity.Property(e => e.Title).HasColumnName("title").HasMaxLength(250).IsRequired();
        entity.Property(e => e.Message).HasColumnName("message").HasMaxLength(2000).IsRequired();
        entity.Property(e => e.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
        entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
    }
}
