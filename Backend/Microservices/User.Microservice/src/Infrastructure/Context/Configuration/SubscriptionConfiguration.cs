using System.Text.Json;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> entity)
    {
        entity.HasKey(e => e.Id).HasName("subscriptions_pkey");

        entity.ToTable("subscriptions");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.Name).HasColumnName("name");
        entity.Property(e => e.Cost).HasColumnName("cost").HasColumnType("real");
        entity.Property(e => e.DurationMonths)
            .HasColumnName("duration_months")
            .HasColumnType("integer")
            .HasDefaultValue(1);
        entity.Property(e => e.MeAiCoin).HasColumnName("me_ai_coin").HasColumnType("numeric(18,2)");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");

        var limitsJsonOptions = new JsonSerializerOptions();
        var limitsComparer = new ValueComparer<SubscriptionLimits?>(
            (left, right) => SubscriptionLimitsEquals(left, right),
            value => SubscriptionLimitsHashCode(value),
            value => SubscriptionLimitsSnapshot(value));

        entity.Property(e => e.Limits)
            .HasColumnName("limits")
            .HasColumnType("jsonb")
            .HasConversion(
                limits => limits == null ? null : JsonSerializer.Serialize(limits, limitsJsonOptions),
                json => string.IsNullOrWhiteSpace(json)
                    ? null
                    : JsonSerializer.Deserialize<SubscriptionLimits>(json, limitsJsonOptions))
            .Metadata.SetValueComparer(limitsComparer);
    }

    private static bool SubscriptionLimitsEquals(SubscriptionLimits? left, SubscriptionLimits? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.NumberOfSocialAccounts == right.NumberOfSocialAccounts
            && left.RateLimitForContentCreation == right.RateLimitForContentCreation
            && left.NumberOfWorkspaces == right.NumberOfWorkspaces;
    }

    private static int SubscriptionLimitsHashCode(SubscriptionLimits? value)
    {
        if (value is null)
        {
            return 0;
        }

        return HashCode.Combine(
            value.NumberOfSocialAccounts,
            value.RateLimitForContentCreation,
            value.NumberOfWorkspaces);
    }

    private static SubscriptionLimits? SubscriptionLimitsSnapshot(SubscriptionLimits? value)
    {
        if (value is null)
        {
            return null;
        }

        return new SubscriptionLimits
        {
            NumberOfSocialAccounts = value.NumberOfSocialAccounts,
            RateLimitForContentCreation = value.RateLimitForContentCreation,
            NumberOfWorkspaces = value.NumberOfWorkspaces
        };
    }
}
