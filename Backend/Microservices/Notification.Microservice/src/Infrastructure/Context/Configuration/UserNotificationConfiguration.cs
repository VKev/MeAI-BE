using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> entity)
    {
        entity.HasKey(e => e.Id).HasName("user_notifications_pkey");

        entity.ToTable("user_notifications");

        entity.HasIndex(e => new { e.NotificationId, e.UserId }, "ux_user_notifications_notification_user")
            .IsUnique();

        entity.HasIndex(e => new { e.UserId, e.IsRead, e.CreatedAt }, "ix_user_notifications_user_read_created_at")
            .IsDescending(false, false, true);

        entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "ix_user_notifications_user_created_at")
            .IsDescending(false, true);

        entity.HasIndex(e => new { e.UserId, e.WasOnlineWhenCreated }, "ix_user_notifications_user_online_snapshot");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.NotificationId).HasColumnName("notification_id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.IsRead).HasColumnName("is_read");
        entity.Property(e => e.ReadAt).HasColumnName("read_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.WasOnlineWhenCreated).HasColumnName("was_online_when_created");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");

        entity.HasOne(e => e.Notification)
            .WithMany(notification => notification.UserNotifications)
            .HasForeignKey(e => e.NotificationId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("user_notifications_notification_id_fkey");
    }
}
