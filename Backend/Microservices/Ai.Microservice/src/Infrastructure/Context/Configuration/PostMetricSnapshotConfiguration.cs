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
                "ck_post_metric_snapshots_view_count_nonnegative",
                "view_count IS NULL OR view_count >= 0");
            tableBuilder.HasCheckConstraint(
                "ck_post_metric_snapshots_like_count_nonnegative",
                "like_count IS NULL OR like_count >= 0");
            tableBuilder.HasCheckConstraint(
                "ck_post_metric_snapshots_comment_count_nonnegative",
                "comment_count IS NULL OR comment_count >= 0");
            tableBuilder.HasCheckConstraint(
                "ck_post_metric_snapshots_reply_count_nonnegative",
                "reply_count IS NULL OR reply_count >= 0");
            tableBuilder.HasCheckConstraint(
                "ck_post_metric_snapshots_share_count_nonnegative",
                "share_count IS NULL OR share_count >= 0");
            tableBuilder.HasCheckConstraint(
                "ck_post_metric_snapshots_repost_count_nonnegative",
                "repost_count IS NULL OR repost_count >= 0");
            tableBuilder.HasCheckConstraint(
                "ck_post_metric_snapshots_quote_count_nonnegative",
                "quote_count IS NULL OR quote_count >= 0");
        });

        entity.HasIndex(
                e => new { e.UserId, e.SocialMediaId, e.PlatformPostId },
                "ux_post_metric_snapshots_user_social_post")
            .IsUnique();

        entity.HasIndex(
            e => new { e.UserId, e.SocialMediaId, e.RetrievedAt },
            "ix_post_metric_snapshots_user_social_retrieved_at");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.SocialMediaId).HasColumnName("social_media_id");
        entity.Property(e => e.Platform).HasColumnName("platform").IsRequired();
        entity.Property(e => e.PlatformPostId).HasColumnName("platform_post_id").IsRequired();
        entity.Property(e => e.PostPayloadJson).HasColumnName("post_payload_json").HasColumnType("jsonb");
        entity.Property(e => e.ViewCount).HasColumnName("view_count");
        entity.Property(e => e.LikeCount).HasColumnName("like_count");
        entity.Property(e => e.CommentCount).HasColumnName("comment_count");
        entity.Property(e => e.ReplyCount).HasColumnName("reply_count");
        entity.Property(e => e.ShareCount).HasColumnName("share_count");
        entity.Property(e => e.RepostCount).HasColumnName("repost_count");
        entity.Property(e => e.RawMetricsJson).HasColumnName("raw_metrics_json").HasColumnType("jsonb");
        entity.Property(e => e.QuoteCount).HasColumnName("quote_count");
        entity.Property(e => e.RetrievedAt).HasColumnName("retrieved_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
    }
}
