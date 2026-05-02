using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class PostBuilderConfiguration : IEntityTypeConfiguration<PostBuilder>
{
    public void Configure(EntityTypeBuilder<PostBuilder> entity)
    {
        entity.HasKey(e => e.Id).HasName("post_builders_pkey");

        entity.ToTable("post_builders");

        entity.HasIndex(e => new { e.UserId, e.WorkspaceId, e.CreatedAt }, "ix_post_builders_user_workspace_created_at")
            .IsDescending(false, false, true);

        entity.HasIndex(e => e.WorkspaceId, "ix_post_builders_workspace_id");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id");
        entity.Property(e => e.OriginKind).HasColumnName("origin_kind");
        entity.Property(e => e.PostType).HasColumnName("post_type");
        entity.Property(e => e.ResourceIds).HasColumnName("resource_ids").HasColumnType("jsonb");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
    }
}
