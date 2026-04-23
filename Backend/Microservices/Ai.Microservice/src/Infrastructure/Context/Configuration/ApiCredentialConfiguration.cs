using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class ApiCredentialConfiguration : IEntityTypeConfiguration<ApiCredential>
{
    public void Configure(EntityTypeBuilder<ApiCredential> entity)
    {
        entity.HasKey(e => e.Id).HasName("api_credentials_pkey");

        entity.ToTable("api_credentials");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.ServiceName).HasColumnName("service_name").HasMaxLength(64).IsRequired();
        entity.Property(e => e.Provider).HasColumnName("provider").HasMaxLength(128).IsRequired();
        entity.Property(e => e.KeyName).HasColumnName("key_name").HasMaxLength(128).IsRequired();
        entity.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(256).IsRequired();
        entity.Property(e => e.ValueEncrypted).HasColumnName("value_encrypted").IsRequired();
        entity.Property(e => e.ValueLast4).HasColumnName("value_last4").HasMaxLength(16);
        entity.Property(e => e.IsActive).HasColumnName("is_active");
        entity.Property(e => e.Source).HasColumnName("source").HasMaxLength(32).IsRequired();
        entity.Property(e => e.Version).HasColumnName("version");
        entity.Property(e => e.LastSyncedFromEnvAt).HasColumnName("last_synced_from_env_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.LastRotatedAt).HasColumnName("last_rotated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");

        entity.HasIndex(e => new { e.ServiceName, e.Provider, e.KeyName })
            .HasDatabaseName("ix_api_credentials_service_provider_key")
            .IsUnique();
    }
}
