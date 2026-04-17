using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class PostHashtagConfiguration : IEntityTypeConfiguration<PostHashtag>
{
    public void Configure(EntityTypeBuilder<PostHashtag> entity)
    {
        entity.HasKey(e => e.Id).HasName("post_hashtags_pkey");
        entity.ToTable("post_hashtags");

        entity.HasIndex(e => e.PostId, "ix_post_hashtags_post_id");
        entity.HasIndex(e => e.HashtagId, "ix_post_hashtags_hashtag_id");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.PostId).HasColumnName("post_id");
        entity.Property(e => e.HashtagId).HasColumnName("hashtag_id");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
    }
}
