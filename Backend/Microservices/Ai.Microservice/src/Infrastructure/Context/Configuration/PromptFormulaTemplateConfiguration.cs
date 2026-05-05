using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class PromptFormulaTemplateConfiguration : IEntityTypeConfiguration<PromptFormulaTemplate>
{
    public void Configure(EntityTypeBuilder<PromptFormulaTemplate> entity)
    {
        entity.ToTable("prompt_formula_templates");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.Key).HasColumnName("key").HasMaxLength(128).IsRequired();
        entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        entity.Property(e => e.Template).HasColumnName("template").HasColumnType("text").IsRequired();
        entity.Property(e => e.OutputType).HasColumnName("output_type").HasMaxLength(64).IsRequired();
        entity.Property(e => e.DefaultLanguage).HasColumnName("default_language").HasMaxLength(32);
        entity.Property(e => e.DefaultInstruction).HasColumnName("default_instruction").HasColumnType("text");
        entity.Property(e => e.IsActive).HasColumnName("is_active");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");

        entity.HasIndex(e => e.Key)
            .IsUnique()
            .HasDatabaseName("ix_prompt_formula_templates_key");

        entity.HasIndex(e => new { e.OutputType, e.IsActive })
            .HasDatabaseName("ix_prompt_formula_templates_output_type_active");
    }
}
