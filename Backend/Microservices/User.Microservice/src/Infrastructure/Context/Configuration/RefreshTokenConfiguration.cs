using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> entity)
    {
        entity.HasKey(e => e.Id).HasName("refresh_tokens_pkey");

        entity.ToTable("refresh_tokens");

        entity.HasIndex(e => e.UserId, "refresh_tokens_user_id_idx");
        entity.HasIndex(e => e.TokenHash, "refresh_tokens_token_hash_key").IsUnique();
        entity.HasIndex(e => e.AccessTokenJti, "refresh_tokens_access_token_jti_key").IsUnique();

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.TokenHash).HasColumnName("token_hash").IsRequired();
        entity.Property(e => e.AccessTokenJti).HasColumnName("access_token_jti");
        entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.AccessTokenRevokedAt).HasColumnName("access_token_revoked_at").HasColumnType("timestamp with time zone");

        entity.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .HasConstraintName("refresh_tokens_user_id_fkey");
    }
}
