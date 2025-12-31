using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> entity)
    {
        entity.HasKey(e => e.Id).HasName("user_subscriptions_pkey");

        entity.ToTable("user_subscriptions");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.SubscriptionId).HasColumnName("subscription_id");
        entity.Property(e => e.ActiveDate).HasColumnName("active_date").HasColumnType("timestamp with time zone");
        entity.Property(e => e.EndDate).HasColumnName("end_date").HasColumnType("timestamp with time zone");
        entity.Property(e => e.Status).HasColumnName("status");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");

        entity.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .HasConstraintName("user_subscriptions_user_id_fkey");

        entity.HasOne<Subscription>()
            .WithMany()
            .HasForeignKey(d => d.SubscriptionId)
            .HasConstraintName("user_subscriptions_subscription_id_fkey");
    }
}
