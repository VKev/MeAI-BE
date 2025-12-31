using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> entity)
    {
        entity.HasKey(e => e.Id).HasName("roles_pkey");

        entity.ToTable("roles");

        entity.HasIndex(e => e.Name, "roles_name_key").IsUnique();

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.Name).HasColumnName("name").IsRequired();
        entity.Property(e => e.Description).HasColumnName("description");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");
    }
}
