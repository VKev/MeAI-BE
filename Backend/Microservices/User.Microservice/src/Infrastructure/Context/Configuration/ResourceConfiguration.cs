using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> entity)
    {
        entity.HasKey(e => e.Id).HasName("resources_pkey");

        entity.ToTable("resources");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id");
        entity.Property(e => e.Link).HasColumnName("link").IsRequired();
        entity.Property(e => e.Status).HasColumnName("status");
        entity.Property(e => e.ResourceType).HasColumnName("type");
        entity.Property(e => e.ContentType).HasColumnName("content_type");
        entity.Property(e => e.SizeBytes).HasColumnName("size_bytes").HasColumnType("bigint");
        entity.Property(e => e.StorageProvider).HasColumnName("storage_provider");
        entity.Property(e => e.StorageBucket).HasColumnName("storage_bucket");
        entity.Property(e => e.StorageRegion).HasColumnName("storage_region");
        entity.Property(e => e.StorageNamespace).HasColumnName("storage_namespace");
        entity.Property(e => e.StorageKey).HasColumnName("storage_key");
        entity.Property(e => e.OriginalFileName).HasColumnName("original_file_name");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");

        entity.HasIndex(e => new { e.UserId, e.WorkspaceId, e.CreatedAt }, "ix_resources_user_workspace_created_at");
        entity.HasIndex(e => new { e.UserId, e.IsDeleted }, "ix_resources_user_deleted");
        entity.HasIndex(e => new { e.StorageNamespace, e.StorageKey }, "ix_resources_storage_namespace_key");

        entity.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .HasConstraintName("resources_user_id_fkey");
    }
}
