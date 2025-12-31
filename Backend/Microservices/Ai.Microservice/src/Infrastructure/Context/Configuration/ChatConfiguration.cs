using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class ChatConfiguration : IEntityTypeConfiguration<Chat>
{
    public void Configure(EntityTypeBuilder<Chat> entity)
    {
        entity.HasKey(e => e.Id).HasName("chats_pkey");

        entity.ToTable("chats");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.SessionId).HasColumnName("session_id");
        entity.Property(e => e.Prompt).HasColumnName("prompt");
        entity.Property(e => e.Config).HasColumnName("config").HasColumnType("json");
        entity.Property(e => e.ReferenceResourceIds).HasColumnName("reference_resource_ids").HasColumnType("json");
        entity.Property(e => e.ResultResourceIds).HasColumnName("result_resource_ids").HasColumnType("json");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");

        entity.HasOne<ChatSession>()
            .WithMany()
            .HasForeignKey(d => d.SessionId)
            .HasConstraintName("chats_session_id_fkey");
    }
}
