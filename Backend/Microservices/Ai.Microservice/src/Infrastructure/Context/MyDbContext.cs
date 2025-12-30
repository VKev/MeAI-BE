using System.Text.Json;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
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
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<Post>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("posts_pkey");

            entity.ToTable("posts");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.SocialMediaId).HasColumnName("social_media_id");
            entity.Property(e => e.Title).HasColumnName("title");

            var contentJsonOptions = new JsonSerializerOptions();
            var contentComparer = new ValueComparer<PostContent?>(
                (left, right) => PostContentEquals(left, right),
                value => PostContentHashCode(value),
                value => PostContentSnapshot(value));

            entity.Property(e => e.Content)
                .HasColumnName("content")
                .HasColumnType("jsonb")
                .HasConversion(
                    content => content == null ? null : JsonSerializer.Serialize(content, contentJsonOptions),
                    json => string.IsNullOrWhiteSpace(json)
                        ? null
                        : JsonSerializer.Deserialize<PostContent>(json, contentJsonOptions))
                .Metadata.SetValueComparer(contentComparer);

            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<PostResource>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("post_resources_pkey");

            entity.ToTable("post_resources");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PostId).HasColumnName("post_id");
            entity.Property(e => e.ResourceId).HasColumnName("resource_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");

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
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
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
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");

            entity.HasOne(d => d.Session).WithMany(p => p.Chats)
                .HasForeignKey(d => d.SessionId)
                .HasConstraintName("chats_session_id_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);

    private static bool PostContentEquals(PostContent? left, PostContent? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (!string.Equals(left.Content, right.Content, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(left.Hashtag, right.Hashtag, StringComparison.Ordinal))
        {
            return false;
        }

        if (left.ResourceList is null && right.ResourceList is null)
        {
            return true;
        }

        if (left.ResourceList is null || right.ResourceList is null)
        {
            return false;
        }

        return left.ResourceList.SequenceEqual(right.ResourceList);
    }

    private static int PostContentHashCode(PostContent? value)
    {
        if (value is null)
        {
            return 0;
        }

        var hash = new HashCode();
        hash.Add(value.Content);
        hash.Add(value.Hashtag);

        if (value.ResourceList is not null)
        {
            foreach (var item in value.ResourceList)
            {
                hash.Add(item);
            }
        }

        return hash.ToHashCode();
    }

    private static PostContent? PostContentSnapshot(PostContent? value)
    {
        if (value is null)
        {
            return null;
        }

        return new PostContent
        {
            Content = value.Content,
            Hashtag = value.Hashtag,
            ResourceList = value.ResourceList is null ? null : new List<string>(value.ResourceList)
        };
    }
}
