using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class HashtagConfiguration : IEntityTypeConfiguration<Hashtag>
{
    public void Configure(EntityTypeBuilder<Hashtag> entity)
    {
        entity.HasKey(e => e.Id).HasName("hashtags_pkey");
        entity.ToTable("hashtags");

        entity.HasIndex(e => e.Name, "ix_hashtags_name").IsUnique();

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.Name).HasColumnName("name");
        entity.Property(e => e.PostCount).HasColumnName("post_count");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
    }
}
