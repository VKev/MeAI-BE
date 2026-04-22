using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class CoinTransactionConfiguration : IEntityTypeConfiguration<CoinTransaction>
{
    public void Configure(EntityTypeBuilder<CoinTransaction> entity)
    {
        entity.ToTable("coin_transactions");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.Delta).HasColumnName("delta").HasColumnType("numeric(18,2)");
        entity.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(128).IsRequired();
        entity.Property(e => e.ReferenceType).HasColumnName("reference_type").HasMaxLength(64);
        entity.Property(e => e.ReferenceId).HasColumnName("reference_id").HasMaxLength(128);
        entity.Property(e => e.BalanceAfter).HasColumnName("balance_after").HasColumnType("numeric(18,2)");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at");

        entity.HasIndex(e => new { e.UserId, e.CreatedAt }).HasDatabaseName("ix_coin_transactions_user_created");
        // Dedupe guard for refunds: don't double-refund the same reference.
        entity.HasIndex(e => new { e.Reason, e.ReferenceType, e.ReferenceId })
            .HasDatabaseName("ix_coin_transactions_ref");
    }
}
