using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public sealed class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .HasConstraintName("user_subscriptions_user_id_fkey");

        builder.HasOne<Subscription>()
            .WithMany()
            .HasForeignKey(e => e.SubscriptionId)
            .HasConstraintName("user_subscriptions_subscription_id_fkey");
    }
}
