using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> entity)
    {
        entity.HasKey(e => e.Id).HasName("workspaces_pkey");

        entity.ToTable("workspaces");

        entity.HasIndex(e => new { e.UserId, e.CreatedAt, e.Id }, "workspaces_user_id_created_at_id_idx");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.Name).HasColumnName("name").IsRequired();
        entity.Property(e => e.Type).HasColumnName("type");
        entity.Property(e => e.Description).HasColumnName("description");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");

        entity.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .HasConstraintName("workspaces_user_id_fkey");
    }
}
