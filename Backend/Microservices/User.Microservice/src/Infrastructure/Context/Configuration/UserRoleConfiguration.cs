using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> entity)
    {
        entity.HasKey(e => e.Id).HasName("user_roles_pkey");

        entity.ToTable("user_roles");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.RoleId).HasColumnName("role_id");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");

        entity.HasOne<Role>()
            .WithMany()
            .HasForeignKey(d => d.RoleId)
            .HasConstraintName("user_roles_role_id_fkey");

        entity.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .HasConstraintName("user_roles_user_id_fkey");
    }
}
