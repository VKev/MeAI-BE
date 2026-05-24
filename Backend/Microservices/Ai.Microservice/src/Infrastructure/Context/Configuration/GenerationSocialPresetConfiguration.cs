using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class GenerationSocialPresetConfiguration : IEntityTypeConfiguration<GenerationSocialPreset>
{
    public void Configure(EntityTypeBuilder<GenerationSocialPreset> entity)
    {
        entity.ToTable("generation_social_presets");

        entity.HasKey(e => e.Id).HasName("generation_social_presets_pkey");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.Mode).HasColumnName("mode").HasMaxLength(16).IsRequired();
        entity.Property(e => e.Platform).HasColumnName("platform").HasMaxLength(32).IsRequired();
        entity.Property(e => e.Label).HasColumnName("label").HasMaxLength(64).IsRequired();
        entity.Property(e => e.ContentType).HasColumnName("content_type").HasMaxLength(32).IsRequired();
        entity.Property(e => e.ContentLabel).HasColumnName("content_label").HasMaxLength(64).IsRequired();
        entity.Property(e => e.SupportedRatiosJson)
            .HasColumnName("supported_ratios")
            .HasColumnType("jsonb")
            .IsRequired();
        entity.Property(e => e.DefaultRatio).HasColumnName("default_ratio").HasMaxLength(16).IsRequired();
        entity.Property(e => e.IsActive).HasColumnName("is_active");
        entity.Property(e => e.SortOrder).HasColumnName("sort_order");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");

        entity.HasIndex(e => new { e.Mode, e.Platform, e.ContentType })
            .HasDatabaseName("ix_generation_social_presets_identity");

        entity.HasIndex(e => new { e.Mode, e.IsActive, e.SortOrder })
            .HasDatabaseName("ix_generation_social_presets_mode_active_sort");
    }
}
