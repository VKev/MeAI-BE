using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> entity)
    {
        entity.HasKey(e => e.Id).HasName("comments_pkey");
        entity.ToTable("comments");

        entity.HasIndex(e => e.PostId, "ix_comments_post_id");
        entity.HasIndex(e => e.UserId, "ix_comments_user_id");
        entity.HasIndex(e => new { e.PostId, e.ParentCommentId, e.CreatedAt, e.Id }, "ix_comments_post_parent_created_at_id");
        entity.HasIndex(e => new { e.ParentCommentId, e.CreatedAt, e.Id }, "ix_comments_parent_created_at_id");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.PostId).HasColumnName("post_id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.ParentCommentId).HasColumnName("parent_comment_id");
        entity.Property(e => e.Content).HasColumnName("content");
        entity.Property(e => e.LikesCount).HasColumnName("likes_count");
        entity.Property(e => e.RepliesCount).HasColumnName("replies_count");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");
    }
}
