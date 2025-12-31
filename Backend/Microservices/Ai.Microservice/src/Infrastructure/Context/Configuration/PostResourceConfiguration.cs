using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class PostResourceConfiguration : IEntityTypeConfiguration<PostResource>
{
    public void Configure(EntityTypeBuilder<PostResource> entity)
    {
        entity.HasKey(e => e.Id).HasName("post_resources_pkey");

        entity.ToTable("post_resources");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.PostId).HasColumnName("post_id");
        entity.Property(e => e.ResourceId).HasColumnName("resource_id");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");

        entity.HasOne<Post>()
            .WithMany()
            .HasForeignKey(d => d.PostId)
            .HasConstraintName("post_resources_post_id_fkey");
    }
}
