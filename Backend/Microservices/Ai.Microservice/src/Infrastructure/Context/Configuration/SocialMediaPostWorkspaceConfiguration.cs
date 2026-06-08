using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class SocialMediaPostWorkspaceConfiguration : IEntityTypeConfiguration<SocialMediaPostWorkspace>
{
    public void Configure(EntityTypeBuilder<SocialMediaPostWorkspace> entity)
    {
        entity.HasKey(e => e.Id).HasName("social_media_post_workspaces_pkey");

        entity.ToTable("social_media_post_workspaces");

        entity.HasIndex(
                e => new { e.UserId, e.WorkspaceId, e.SocialMediaId, e.PostId },
                "ux_social_media_post_workspaces_user_workspace_social_post")
            .IsUnique();

        entity.HasIndex(
            e => new { e.UserId, e.WorkspaceId, e.PostId },
            "ix_social_media_post_workspaces_user_workspace_post");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.PostId).HasColumnName("post_id");
        entity.Property(e => e.SocialMediaId).HasColumnName("social_media_id");
        entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");

        entity.HasOne<Post>()
            .WithMany()
            .HasForeignKey(e => e.PostId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("social_media_post_workspaces_post_id_fkey");
    }
}
