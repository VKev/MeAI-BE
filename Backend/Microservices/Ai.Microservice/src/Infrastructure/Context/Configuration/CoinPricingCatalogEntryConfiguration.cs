using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class CoinPricingCatalogEntryConfiguration : IEntityTypeConfiguration<CoinPricingCatalogEntry>
{
    public void Configure(EntityTypeBuilder<CoinPricingCatalogEntry> entity)
    {
        entity.ToTable("coin_pricing_catalog");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.ActionType).HasColumnName("action_type").HasMaxLength(64).IsRequired();
        entity.Property(e => e.Model).HasColumnName("model").HasMaxLength(64).IsRequired();
        entity.Property(e => e.Variant).HasColumnName("variant").HasMaxLength(64);
        entity.Property(e => e.Unit).HasColumnName("unit").HasMaxLength(32).IsRequired();
        entity.Property(e => e.UnitCostCoins).HasColumnName("unit_cost_coins").HasColumnType("numeric(18,2)");
        entity.Property(e => e.IsActive).HasColumnName("is_active");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        // Lookup key for the pricing resolver: (action, model, variant) → one active entry.
        entity.HasIndex(e => new { e.ActionType, e.Model, e.Variant, e.IsActive })
            .HasDatabaseName("ix_coin_pricing_catalog_lookup");
    }
}
