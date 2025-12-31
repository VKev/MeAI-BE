using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class WorkspaceSocialMediaConfiguration : IEntityTypeConfiguration<WorkspaceSocialMedia>
{
    public void Configure(EntityTypeBuilder<WorkspaceSocialMedia> entity)
    {
        entity.HasKey(e => e.Id).HasName("workspace_social_medias_pkey");

        entity.ToTable("workspace_social_medias");

        entity.HasIndex(e => new { e.UserId, e.WorkspaceId, e.CreatedAt, e.Id }, "workspace_social_medias_user_id_workspace_id_created_at_id_idx");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id");
        entity.Property(e => e.SocialMediaId).HasColumnName("social_media_id");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");

        entity.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .HasConstraintName("workspace_social_medias_user_id_fkey");

        entity.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(d => d.WorkspaceId)
            .HasConstraintName("workspace_social_medias_workspace_id_fkey");

        entity.HasOne<SocialMedia>()
            .WithMany()
            .HasForeignKey(d => d.SocialMediaId)
            .HasConstraintName("workspace_social_medias_social_media_id_fkey");
    }
}
