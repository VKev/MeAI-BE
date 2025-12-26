using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public sealed class WorkspaceSocialMediaConfiguration : IEntityTypeConfiguration<WorkspaceSocialMedia>
{
    public void Configure(EntityTypeBuilder<WorkspaceSocialMedia> builder)
    {
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .HasConstraintName("workspace_social_media_user_id_fkey");

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(e => e.WorkspaceId)
            .HasConstraintName("workspace_social_media_workspace_id_fkey");

        builder.HasOne<SocialMedia>()
            .WithMany()
            .HasForeignKey(e => e.SocialMediaId)
            .HasConstraintName("workspace_social_media_social_media_id_fkey");

        builder.HasIndex(e => new { e.WorkspaceId, e.SocialMediaId });
    }
}
