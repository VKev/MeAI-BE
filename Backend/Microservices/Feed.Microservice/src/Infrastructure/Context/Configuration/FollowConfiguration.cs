using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class FollowConfiguration : IEntityTypeConfiguration<Follow>
{
    public void Configure(EntityTypeBuilder<Follow> entity)
    {
        entity.HasKey(e => e.Id).HasName("follows_pkey");
        entity.ToTable("follows");

        entity.HasIndex(e => e.FollowerId, "ix_follows_follower_id");
        entity.HasIndex(e => e.FolloweeId, "ix_follows_followee_id");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.FollowerId).HasColumnName("follower_id");
        entity.Property(e => e.FolloweeId).HasColumnName("followee_id");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
    }
}
