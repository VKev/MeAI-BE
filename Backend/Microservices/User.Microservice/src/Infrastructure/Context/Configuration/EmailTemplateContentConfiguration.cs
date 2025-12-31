using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class EmailTemplateContentConfiguration : IEntityTypeConfiguration<EmailTemplateContent>
{
    public void Configure(EntityTypeBuilder<EmailTemplateContent> entity)
    {
        entity.HasKey(e => e.Id).HasName("email_template_contents_pkey");

        entity.ToTable("email_template_contents");

        entity.HasIndex(e => e.EmailTemplateId, "email_template_contents_email_template_id_key").IsUnique();

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.EmailTemplateId).HasColumnName("email_template_id");
        entity.Property(e => e.Subject).HasColumnName("subject").IsRequired();
        entity.Property(e => e.HtmlBody).HasColumnName("html_body").IsRequired();
        entity.Property(e => e.TextBody).HasColumnName("text_body");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");

        entity.HasOne<EmailTemplate>()
            .WithOne()
            .HasForeignKey<EmailTemplateContent>(d => d.EmailTemplateId)
            .HasConstraintName("email_template_contents_email_template_id_fkey");
    }
}
