using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Context;

public partial class MyDbContext : DbContext
{
    public MyDbContext()
    {
    }

    public MyDbContext(DbContextOptions<MyDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Chat> Chats { get; set; }

    public virtual DbSet<ChatSession> ChatSessions { get; set; }

    public virtual DbSet<Post> Posts { get; set; }

    public virtual DbSet<PostResource> PostResources { get; set; }

    public virtual DbSet<SocialMedia> SocialMedias { get; set; }

    public virtual DbSet<Workspace> Workspaces { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Workspace>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("workspaces_pkey");

            entity.ToTable("workspaces");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.WorkspaceName).HasColumnName("workspace_name").IsRequired();
            entity.Property(e => e.WorkspaceType).HasColumnName("workspace_type");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp");
        });

        modelBuilder.Entity<SocialMedia>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("social_medias_pkey");

            entity.ToTable("social_medias");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.SocialMediaType).HasColumnName("social_media_type").IsRequired();
            entity.Property(e => e.AccessToken).HasColumnName("access_token");
            entity.Property(e => e.TokenType).HasColumnName("token_type");
            entity.Property(e => e.RefreshToken).HasColumnName("refresh_token");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp");
        });

        modelBuilder.Entity<Post>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("posts_pkey");

            entity.ToTable("posts");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.SocialMediaId).HasColumnName("social_media_id");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp");
        });

        modelBuilder.Entity<PostResource>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("post_resources_pkey");

            entity.ToTable("post_resources");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PostId).HasColumnName("post_id");
            entity.Property(e => e.ResourceId).HasColumnName("resource_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp");

            entity.HasOne(d => d.Post).WithMany(p => p.PostResources)
                .HasForeignKey(d => d.PostId)
                .HasConstraintName("post_resources_post_id_fkey");
        });

        modelBuilder.Entity<ChatSession>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("chat_sessions_pkey");

            entity.ToTable("chat_sessions");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.SessionName).HasColumnName("session_name");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp");
        });

        modelBuilder.Entity<Chat>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("chats_pkey");

            entity.ToTable("chats");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.Prompt).HasColumnName("prompt");
            entity.Property(e => e.Config).HasColumnName("config").HasColumnType("json");
            entity.Property(e => e.ReferenceResourceIds).HasColumnName("reference_resource_ids").HasColumnType("json");
            entity.Property(e => e.ResultResourceIds).HasColumnName("result_resource_ids").HasColumnType("json");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp");

            entity.HasOne(d => d.Session).WithMany(p => p.Chats)
                .HasForeignKey(d => d.SessionId)
                .HasConstraintName("chats_session_id_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
