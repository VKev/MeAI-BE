using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class ConfigConfiguration : IEntityTypeConfiguration<Config>
{
    public void Configure(EntityTypeBuilder<Config> entity)
    {
        entity.HasKey(e => e.Id).HasName("configs_pkey");

        entity.ToTable("configs");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.ChatModel).HasColumnName("chat_model");
        entity.Property(e => e.MediaAspectRatio).HasColumnName("media_aspect_ratio");
        entity.Property(e => e.NumberOfVariances).HasColumnName("number_of_variances");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.IsDeleted).HasColumnName("is_deleted");
    }
}
