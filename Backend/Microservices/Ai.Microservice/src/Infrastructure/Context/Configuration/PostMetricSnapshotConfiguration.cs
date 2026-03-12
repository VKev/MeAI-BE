using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class PostMetricSnapshotConfiguration : IEntityTypeConfiguration<PostMetricSnapshot>
{
    public void Configure(EntityTypeBuilder<PostMetricSnapshot> entity)
    {
        entity.HasKey(e => e.Id).HasName("post_metric_snapshots_pkey");

        entity.ToTable("post_metric_snapshots", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_post_metric_snapshots_metric_window",
                "metric_window IN ('hour', 'day', 'lifetime')");
            tableBuilder.HasCheckConstraint(
                "ck_post_metric_snapshots_view_count_nonnegative",
                "view_count IS NULL OR view_count >= 0");
            tableBuilder.HasCheckConstraint(
                "ck_post_metric_snapshots_like_count_nonnegative",
                "like_count IS NULL OR like_count >= 0");
            tableBuilder.HasCheckConstraint(
                "ck_post_metric_snapshots_comment_count_nonnegative",
                "comment_count IS NULL OR comment_count >= 0");
            tableBuilder.HasCheckConstraint(
                "ck_post_metric_snapshots_share_count_nonnegative",
                "share_count IS NULL OR share_count >= 0");
            tableBuilder.HasCheckConstraint(
                "ck_post_metric_snapshots_save_count_nonnegative",
                "save_count IS NULL OR save_count >= 0");
            tableBuilder.HasCheckConstraint(
                "ck_post_metric_snapshots_impression_count_nonnegative",
                "impression_count IS NULL OR impression_count >= 0");
            tableBuilder.HasCheckConstraint(
                "ck_post_metric_snapshots_reach_count_nonnegative",
                "reach_count IS NULL OR reach_count >= 0");
            tableBuilder.HasCheckConstraint(
                "ck_post_metric_snapshots_watch_time_seconds_nonnegative",
                "watch_time_seconds IS NULL OR watch_time_seconds >= 0");
        });

        entity.HasIndex(
                e => new { e.PostPublicationId, e.CapturedAt, e.MetricWindow },
                "ux_post_metric_snapshots_publication_captured_window")
            .IsUnique();

        entity.HasIndex(
            e => new { e.PostPublicationId, e.CapturedAt },
            "ix_post_metric_snapshots_publication_captured_at")
            .IsDescending(false, true);

        entity.HasIndex(e => e.CapturedAt, "ix_post_metric_snapshots_captured_at");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.PostPublicationId).HasColumnName("post_publication_id");
        entity.Property(e => e.CapturedAt).HasColumnName("captured_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.MetricWindow).HasColumnName("metric_window").IsRequired();
        entity.Property(e => e.ViewCount).HasColumnName("view_count");
        entity.Property(e => e.LikeCount).HasColumnName("like_count");
        entity.Property(e => e.CommentCount).HasColumnName("comment_count");
        entity.Property(e => e.ShareCount).HasColumnName("share_count");
        entity.Property(e => e.SaveCount).HasColumnName("save_count");
        entity.Property(e => e.ImpressionCount).HasColumnName("impression_count");
        entity.Property(e => e.ReachCount).HasColumnName("reach_count");
        entity.Property(e => e.WatchTimeSeconds).HasColumnName("watch_time_seconds");
        entity.Property(e => e.RawMetrics).HasColumnName("raw_metrics").HasColumnType("jsonb");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");

        entity.HasOne<PostPublication>()
            .WithMany()
            .HasForeignKey(d => d.PostPublicationId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("post_metric_snapshots_post_publication_id_fkey");
    }
}
