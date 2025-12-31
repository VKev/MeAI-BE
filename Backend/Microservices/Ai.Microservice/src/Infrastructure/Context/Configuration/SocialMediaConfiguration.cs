using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class SocialMediaConfiguration : IEntityTypeConfiguration<SocialMedia>
{
    public void Configure(EntityTypeBuilder<SocialMedia> entity)
    {
        entity.HasKey(e => e.Id).HasName("social_medias_pkey");

        entity.ToTable("social_medias");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.SocialMediaType).HasColumnName("social_media_type").IsRequired();
        entity.Property(e => e.AccessToken).HasColumnName("access_token");
        entity.Property(e => e.TokenType).HasColumnName("token_type");
        entity.Property(e => e.RefreshToken).HasColumnName("refresh_token");
        entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
    }
}
