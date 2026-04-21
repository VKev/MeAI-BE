using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class PostPublicationConfiguration : IEntityTypeConfiguration<PostPublication>
{
    public void Configure(EntityTypeBuilder<PostPublication> entity)
    {
        entity.HasKey(e => e.Id).HasName("post_publications_pkey");

        entity.ToTable("post_publications", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_post_publications_external_content_id_type",
                "external_content_id_type IN ('post_id', 'publish_id')");
            tableBuilder.HasCheckConstraint(
                "ck_post_publications_publish_status",
                "publish_status IN ('processing', 'published', 'unpublishing', 'failed')");
        });

        entity.HasIndex(
                e => new { e.SocialMediaType, e.DestinationOwnerId, e.ExternalContentId },
                "ux_post_publications_external_content")
            .IsUnique();

        entity.HasIndex(e => new { e.PostId, e.CreatedAt }, "ix_post_publications_post_created_at")
            .IsDescending(false, true);

        entity.HasIndex(e => new { e.WorkspaceId, e.PublishedAt }, "ix_post_publications_workspace_published_at")
            .IsDescending(false, true);

        entity.HasIndex(
            e => new { e.PublishStatus, e.LastMetricsSyncAt },
            "ix_post_publications_publish_status_last_metrics_sync_at");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.PostId).HasColumnName("post_id");
        entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id");
        entity.Property(e => e.SocialMediaId).HasColumnName("social_media_id");
        entity.Property(e => e.SocialMediaType).HasColumnName("social_media_type").IsRequired();
        entity.Property(e => e.DestinationOwnerId).HasColumnName("destination_owner_id").IsRequired();
        entity.Property(e => e.ExternalContentId).HasColumnName("external_content_id").IsRequired();
        entity.Property(e => e.ExternalContentIdType).HasColumnName("external_content_id_type").IsRequired();
        entity.Property(e => e.ContentType).HasColumnName("content_type").IsRequired();
        entity.Property(e => e.PublishStatus).HasColumnName("publish_status").IsRequired();
        entity.Property(e => e.PublishedAt).HasColumnName("published_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.LastMetricsSyncAt).HasColumnName("last_metrics_sync_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");

        entity.HasOne<Post>()
            .WithMany()
            .HasForeignKey(d => d.PostId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("post_publications_post_id_fkey");

    }
}
