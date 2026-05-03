using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class CoinPackageConfiguration : IEntityTypeConfiguration<CoinPackage>
{
    public void Configure(EntityTypeBuilder<CoinPackage> entity)
    {
        entity.ToTable("coin_packages");

        entity.HasKey(e => e.Id)
            .HasName("coin_packages_pkey");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        entity.Property(e => e.CoinAmount).HasColumnName("coin_amount").HasColumnType("numeric(18,2)");
        entity.Property(e => e.BonusCoins).HasColumnName("bonus_coins").HasColumnType("numeric(18,2)");
        entity.Property(e => e.Price).HasColumnName("price").HasColumnType("numeric(18,2)");
        entity.Property(e => e.Currency).HasColumnName("currency").HasMaxLength(8).IsRequired();
        entity.Property(e => e.IsActive).HasColumnName("is_active");
        entity.Property(e => e.DisplayOrder).HasColumnName("display_order");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");

        entity.HasIndex(e => new { e.IsActive, e.DisplayOrder })
            .HasDatabaseName("ix_coin_packages_active_display_order");
    }
}
