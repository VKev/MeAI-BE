using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class GenerationModelOptionConfiguration : IEntityTypeConfiguration<GenerationModelOption>
{
    public void Configure(EntityTypeBuilder<GenerationModelOption> entity)
    {
        entity.ToTable("generation_model_options");

        entity.HasKey(e => e.Id).HasName("generation_model_options_pkey");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.Mode).HasColumnName("mode").HasMaxLength(16).IsRequired();
        entity.Property(e => e.ModelId).HasColumnName("model_id").HasMaxLength(128).IsRequired();
        entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(512);
        entity.Property(e => e.SupportedRatiosJson)
            .HasColumnName("supported_ratios")
            .HasColumnType("jsonb")
            .IsRequired();
        entity.Property(e => e.SupportedQualitiesJson)
            .HasColumnName("supported_qualities")
            .HasColumnType("jsonb")
            .IsRequired();
        entity.Property(e => e.SupportsResolution).HasColumnName("supports_resolution");
        entity.Property(e => e.IsActive).HasColumnName("is_active");
        entity.Property(e => e.SortOrder).HasColumnName("sort_order");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");

        entity.HasIndex(e => new { e.Mode, e.ModelId })
            .HasDatabaseName("ix_generation_model_options_mode_model_id");

        entity.HasIndex(e => new { e.Mode, e.IsActive, e.SortOrder })
            .HasDatabaseName("ix_generation_model_options_mode_active_sort");
    }
}
