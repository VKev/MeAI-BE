using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class PostLikeConfiguration : IEntityTypeConfiguration<PostLike>
{
    public void Configure(EntityTypeBuilder<PostLike> entity)
    {
        entity.HasKey(e => e.Id).HasName("post_likes_pkey");
        entity.ToTable("post_likes");

        entity.HasIndex(e => e.PostId, "ix_post_likes_post_id");
        entity.HasIndex(e => e.UserId, "ix_post_likes_user_id");
        entity.HasIndex(e => new { e.PostId, e.UserId }, "ix_post_likes_post_id_user_id").IsUnique();

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.PostId).HasColumnName("post_id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
    }
}
