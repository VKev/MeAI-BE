using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class SocialAccountAnalysisSuggestionConfiguration : IEntityTypeConfiguration<SocialAccountAnalysisSuggestion>
{
    public void Configure(EntityTypeBuilder<SocialAccountAnalysisSuggestion> entity)
    {
        entity.HasKey(e => e.Id).HasName("social_account_analysis_suggestions_pkey");
        entity.ToTable("social_account_analysis_suggestions");

        entity.HasIndex(e => e.CorrelationId, "ux_social_account_analysis_suggestions_correlation_id").IsUnique();
        entity.HasIndex(e => new { e.UserId, e.SocialMediaId }, "ux_social_account_analysis_suggestions_user_social_media")
            .IsUnique();

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.CorrelationId).HasColumnName("correlation_id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.SocialMediaId).HasColumnName("social_media_id");
        entity.Property(e => e.Platform).HasColumnName("platform").HasMaxLength(32).IsRequired();
        entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        entity.Property(e => e.Suggestion).HasColumnName("suggestion").HasColumnType("text");
        entity.Property(e => e.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
        entity.Property(e => e.ErrorMessage).HasColumnName("error_message").HasColumnType("text");
        entity.Property(e => e.RequestJson).HasColumnName("request_json").HasColumnType("jsonb");
        entity.Property(e => e.ResponseJson).HasColumnName("response_json").HasColumnType("jsonb");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamp with time zone");
    }
}
