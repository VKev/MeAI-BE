using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class CommentLikeConfiguration : IEntityTypeConfiguration<CommentLike>
{
    public void Configure(EntityTypeBuilder<CommentLike> entity)
    {
        entity.HasKey(e => e.Id).HasName("comment_likes_pkey");
        entity.ToTable("comment_likes");

        entity.HasIndex(e => e.CommentId, "ix_comment_likes_comment_id");
        entity.HasIndex(e => e.UserId, "ix_comment_likes_user_id");
        entity.HasIndex(e => new { e.CommentId, e.UserId }, "ix_comment_likes_comment_user_id").IsUnique();

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.CommentId).HasColumnName("comment_id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
    }
}
