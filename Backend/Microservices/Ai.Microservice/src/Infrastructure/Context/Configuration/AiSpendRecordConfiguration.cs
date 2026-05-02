using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class AiSpendRecordConfiguration : IEntityTypeConfiguration<AiSpendRecord>
{
    public void Configure(EntityTypeBuilder<AiSpendRecord> entity)
    {
        entity.HasKey(e => e.Id).HasName("ai_spend_records_pkey");

        entity.ToTable("ai_spend_records");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id");
        entity.Property(e => e.Provider).HasColumnName("provider").HasMaxLength(64).IsRequired();
        entity.Property(e => e.ActionType).HasColumnName("action_type").HasMaxLength(64).IsRequired();
        entity.Property(e => e.Model).HasColumnName("model").HasMaxLength(128).IsRequired();
        entity.Property(e => e.Variant).HasColumnName("variant").HasMaxLength(64);
        entity.Property(e => e.Unit).HasColumnName("unit").HasMaxLength(64).IsRequired();
        entity.Property(e => e.Quantity).HasColumnName("quantity");
        entity.Property(e => e.UnitCostCoins).HasColumnName("unit_cost_coins").HasColumnType("numeric(18,2)");
        entity.Property(e => e.TotalCoins).HasColumnName("total_coins").HasColumnType("numeric(18,2)");
        entity.Property(e => e.ReferenceType).HasColumnName("reference_type").HasMaxLength(64).IsRequired();
        entity.Property(e => e.ReferenceId).HasColumnName("reference_id").HasMaxLength(128).IsRequired();
        entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");

        entity.HasIndex(e => e.CreatedAt).HasDatabaseName("ix_ai_spend_records_created_at");
        entity.HasIndex(e => new { e.ActionType, e.Model, e.CreatedAt })
            .HasDatabaseName("ix_ai_spend_records_action_model_created_at");
        entity.HasIndex(e => new { e.ReferenceType, e.ReferenceId })
            .HasDatabaseName("ix_ai_spend_records_reference");
        entity.HasIndex(e => new { e.UserId, e.CreatedAt })
            .HasDatabaseName("ix_ai_spend_records_user_created_at");
    }
}
