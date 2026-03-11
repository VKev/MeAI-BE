using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class PostAnalyticsSnapshotConfiguration : IEntityTypeConfiguration<PostAnalyticsSnapshot>
{
    public void Configure(EntityTypeBuilder<PostAnalyticsSnapshot> entity)
    {
        entity.HasKey(e => e.Id).HasName("post_analytics_snapshots_pkey");

        entity.ToTable("post_analytics_snapshots");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.PostId).HasColumnName("post_id");
        entity.Property(e => e.SocialMediaId).HasColumnName("social_media_id");
        entity.Property(e => e.Platform).HasColumnName("platform");
        entity.Property(e => e.PlatformPostId).HasColumnName("platform_post_id");
        entity.Property(e => e.PostPayloadJson).HasColumnName("post_payload_json").HasColumnType("jsonb");
        entity.Property(e => e.StatsPayloadJson).HasColumnName("stats_payload_json").HasColumnType("jsonb");
        entity.Property(e => e.AnalysisPayloadJson).HasColumnName("analysis_payload_json").HasColumnType("jsonb");
        entity.Property(e => e.RetrievedAt).HasColumnName("retrieved_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");

        entity.HasIndex(e => new { e.UserId, e.SocialMediaId, e.PlatformPostId })
            .IsUnique()
            .HasDatabaseName("ux_post_analytics_snapshots_user_social_post");

        entity.HasIndex(e => new { e.UserId, e.SocialMediaId, e.RetrievedAt })
            .HasDatabaseName("ix_post_analytics_snapshots_user_social_retrieved_at");
    }
}
