using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> entity)
    {
        entity.HasKey(e => e.Id).HasName("email_templates_pkey");

        entity.ToTable("email_templates");

        entity.HasIndex(e => e.Key, "email_templates_key_key").IsUnique();

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.Key).HasColumnName("key").IsRequired();
        entity.Property(e => e.Description).HasColumnName("description");
        entity.Property(e => e.IsActive).HasColumnName("is_active");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");
    }
}
