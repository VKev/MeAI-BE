using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class SocialMediaConfiguration : IEntityTypeConfiguration<SocialMedia>
{
    public void Configure(EntityTypeBuilder<SocialMedia> entity)
    {
        entity.HasKey(e => e.Id).HasName("social_medias_pkey");

        entity.ToTable("social_medias");

        entity.HasIndex(e => new { e.UserId, e.CreatedAt, e.Id }, "social_medias_user_id_created_at_id_idx");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.Type).HasColumnName("type").IsRequired();
        entity.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");

        entity.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .HasConstraintName("social_medias_user_id_fkey");
    }
}
