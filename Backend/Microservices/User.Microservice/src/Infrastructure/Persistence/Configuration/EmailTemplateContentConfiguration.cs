using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public sealed class EmailTemplateContentConfiguration : IEntityTypeConfiguration<EmailTemplateContent>
{
    public void Configure(EntityTypeBuilder<EmailTemplateContent> builder)
    {
        builder.HasIndex(e => e.EmailTemplateId)
            .IsUnique();

        builder.HasOne<EmailTemplate>()
            .WithMany()
            .HasForeignKey(e => e.EmailTemplateId)
            .HasConstraintName("email_template_contents_email_template_id_fkey");
    }
}
