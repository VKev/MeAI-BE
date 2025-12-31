using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> entity)
    {
        entity.HasKey(e => e.Id).HasName("transactions_pkey");

        entity.ToTable("transactions");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.RelationId).HasColumnName("relation_id");
        entity.Property(e => e.RelationType).HasColumnName("relation_type");
        entity.Property(e => e.Cost).HasColumnName("cost").HasColumnType("numeric(18,2)");
        entity.Property(e => e.TransactionType).HasColumnName("type");
        entity.Property(e => e.TokenUsed).HasColumnName("token_used");
        entity.Property(e => e.PaymentMethod).HasColumnName("payment_method");
        entity.Property(e => e.Status).HasColumnName("status");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");

        entity.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .HasConstraintName("transactions_user_id_fkey");
    }
}
