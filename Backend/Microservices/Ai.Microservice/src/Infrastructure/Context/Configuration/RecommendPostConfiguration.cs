using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class RecommendPostConfiguration : IEntityTypeConfiguration<RecommendPost>
{
    public void Configure(EntityTypeBuilder<RecommendPost> entity)
    {
        entity.HasKey(e => e.Id).HasName("recommend_posts_pkey");
        entity.ToTable("recommend_posts");

        entity.HasIndex(e => e.CorrelationId, "ux_recommend_posts_correlation_id").IsUnique();
        // Unique on OriginalPostId enforces 1:1 between Post and RecommendPost. The
        // replace-on-rerun semantic is enforced at the command boundary; this index
        // is the safety net that turns a concurrent double-submit into a 409 instead
        // of a silently-orphaned row.
        entity.HasIndex(e => e.OriginalPostId, "ux_recommend_posts_original_post_id").IsUnique();
        entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "ix_recommend_posts_user_created_at").IsDescending(false, true);

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.CorrelationId).HasColumnName("correlation_id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id");
        entity.Property(e => e.OriginalPostId).HasColumnName("original_post_id");
        entity.Property(e => e.ImproveCaption).HasColumnName("improve_caption").IsRequired();
        entity.Property(e => e.ImproveImage).HasColumnName("improve_image").IsRequired();
        entity.Property(e => e.Style).HasColumnName("style").HasMaxLength(32).IsRequired().HasDefaultValue(DraftPostStyles.Branded);
        entity.Property(e => e.UserInstruction).HasColumnName("user_instruction").HasColumnType("text");
        entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.Property(e => e.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
        entity.Property(e => e.ErrorMessage).HasColumnName("error_message").HasColumnType("text");
        entity.Property(e => e.ResultCaption).HasColumnName("result_caption").HasColumnType("text");
        entity.Property(e => e.ResultResourceId).HasColumnName("result_resource_id");
        entity.Property(e => e.ResultPresignedUrl).HasColumnName("result_presigned_url").HasColumnType("text");
        entity.Property(e => e.ResultReferencesJson).HasColumnName("result_references").HasColumnType("jsonb");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamp with time zone");
    }
}
