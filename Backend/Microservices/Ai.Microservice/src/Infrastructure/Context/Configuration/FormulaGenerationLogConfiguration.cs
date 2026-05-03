using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class FormulaGenerationLogConfiguration : IEntityTypeConfiguration<FormulaGenerationLog>
{
    public void Configure(EntityTypeBuilder<FormulaGenerationLog> entity)
    {
        entity.ToTable("formula_generation_logs");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id");
        entity.Property(e => e.FormulaTemplateId).HasColumnName("formula_template_id");
        entity.Property(e => e.FormulaKeySnapshot).HasColumnName("formula_key_snapshot").HasMaxLength(128);
        entity.Property(e => e.RenderedPrompt).HasColumnName("rendered_prompt").HasColumnType("text").IsRequired();
        entity.Property(e => e.VariablesJson).HasColumnName("variables_json").HasColumnType("text").IsRequired();
        entity.Property(e => e.OutputType).HasColumnName("output_type").HasMaxLength(64).IsRequired();
        entity.Property(e => e.Model).HasColumnName("model").HasMaxLength(128).IsRequired();
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");

        entity.HasIndex(e => e.CreatedAt)
            .HasDatabaseName("ix_formula_generation_logs_created_at");

        entity.HasIndex(e => new { e.UserId, e.CreatedAt })
            .HasDatabaseName("ix_formula_generation_logs_user_created_at");

        entity.HasIndex(e => new { e.FormulaTemplateId, e.CreatedAt })
            .HasDatabaseName("ix_formula_generation_logs_formula_created_at");
    }
}
