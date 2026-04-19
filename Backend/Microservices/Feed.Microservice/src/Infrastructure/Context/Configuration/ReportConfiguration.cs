using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> entity)
    {
        entity.HasKey(e => e.Id).HasName("reports_pkey");
        entity.ToTable("reports");

        entity.HasIndex(e => e.ReporterId, "ix_reports_reporter_id");
        entity.HasIndex(e => new { e.TargetType, e.TargetId }, "ix_reports_target_type_target_id");
        entity.HasIndex(e => e.Status, "ix_reports_status");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.ReporterId).HasColumnName("reporter_id");
        entity.Property(e => e.TargetType).HasColumnName("target_type");
        entity.Property(e => e.TargetId).HasColumnName("target_id");
        entity.Property(e => e.Reason).HasColumnName("reason");
        entity.Property(e => e.Status).HasColumnName("status");
        entity.Property(e => e.ReviewedByAdminId).HasColumnName("reviewed_by_admin_id");
        entity.Property(e => e.ReviewedAt).HasColumnName("reviewed_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.ResolutionNote).HasColumnName("resolution_note");
        entity.Property(e => e.ActionType).HasColumnName("action_type");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
    }
}
