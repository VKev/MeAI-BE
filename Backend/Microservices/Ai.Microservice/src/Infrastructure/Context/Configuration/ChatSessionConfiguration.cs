using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class ChatSessionConfiguration : IEntityTypeConfiguration<ChatSession>
{
    public void Configure(EntityTypeBuilder<ChatSession> entity)
    {
        entity.HasKey(e => e.Id).HasName("chat_sessions_pkey");

        entity.ToTable("chat_sessions");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id");
        entity.Property(e => e.SessionName).HasColumnName("session_name");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");

        entity.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(e => e.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("chat_sessions_workspace_id_fkey");

        entity.HasIndex(e => new { e.UserId, e.WorkspaceId, e.CreatedAt, e.Id }, "chat_sessions_user_id_workspace_id_created_at_id_idx");
    }
}
