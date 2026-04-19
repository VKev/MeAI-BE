using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class PostConfiguration : IEntityTypeConfiguration<Post>
{
    public void Configure(EntityTypeBuilder<Post> entity)
    {
        entity.HasKey(e => e.Id).HasName("posts_pkey");
        entity.ToTable("posts");

        entity.HasIndex(e => e.UserId, "ix_posts_user_id");
        entity.HasIndex(e => new { e.CreatedAt, e.Id }, "ix_posts_created_at_id");
        entity.HasIndex(e => new { e.UserId, e.CreatedAt, e.Id }, "ix_posts_user_created_at_id");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.Content).HasColumnName("content");
        entity.Property(e => e.ResourceIds).HasColumnName("resource_ids").HasColumnType("uuid[]");
        entity.Property(e => e.MediaUrl).HasColumnName("media_url");
        entity.Property(e => e.MediaType).HasColumnName("media_type");
        entity.Property(e => e.LikesCount).HasColumnName("likes_count");
        entity.Property(e => e.CommentsCount).HasColumnName("comments_count");
        entity.Property(e => e.SharesCount).HasColumnName("shares_count");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");
    }
}
