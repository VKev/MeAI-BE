using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class DraftPostTaskConfiguration : IEntityTypeConfiguration<DraftPostTask>
{
    public void Configure(EntityTypeBuilder<DraftPostTask> entity)
    {
        entity.HasKey(e => e.Id).HasName("draft_post_tasks_pkey");
        entity.ToTable("draft_post_tasks");

        entity.HasIndex(e => e.CorrelationId, "ux_draft_post_tasks_correlation_id").IsUnique();
        entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "ix_draft_post_tasks_user_created_at").IsDescending(false, true);

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.CorrelationId).HasColumnName("correlation_id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.SocialMediaId).HasColumnName("social_media_id");
        entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id");
        entity.Property(e => e.UserPrompt).HasColumnName("user_prompt").HasColumnType("text").IsRequired();
        entity.Property(e => e.TopK).HasColumnName("top_k");
        entity.Property(e => e.MaxReferenceImages).HasColumnName("max_reference_images");
        entity.Property(e => e.MaxRagPosts).HasColumnName("max_rag_posts");
        entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.Property(e => e.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
        entity.Property(e => e.ErrorMessage).HasColumnName("error_message").HasColumnType("text");
        entity.Property(e => e.ResultPostBuilderId).HasColumnName("result_post_builder_id");
        entity.Property(e => e.ResultPostId).HasColumnName("result_post_id");
        entity.Property(e => e.ResultResourceId).HasColumnName("result_resource_id");
        entity.Property(e => e.ResultPresignedUrl).HasColumnName("result_presigned_url").HasColumnType("text");
        entity.Property(e => e.ResultCaption).HasColumnName("result_caption").HasColumnType("text");
        entity.Property(e => e.ResultReferencesJson).HasColumnName("result_references").HasColumnType("jsonb");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamp with time zone");
    }
}
