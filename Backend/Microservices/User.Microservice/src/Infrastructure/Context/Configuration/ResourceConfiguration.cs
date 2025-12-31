using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> entity)
    {
        entity.HasKey(e => e.Id).HasName("resources_pkey");

        entity.ToTable("resources");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.Link).HasColumnName("link").IsRequired();
        entity.Property(e => e.Status).HasColumnName("status");
        entity.Property(e => e.ResourceType).HasColumnName("type");
        entity.Property(e => e.ContentType).HasColumnName("content_type");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");

        entity.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .HasConstraintName("resources_user_id_fkey");
    }
}
