using System.Text.Json;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Context.Configuration;

public sealed class PostConfiguration : IEntityTypeConfiguration<Post>
{
    public void Configure(EntityTypeBuilder<Post> entity)
    {
        entity.HasKey(e => e.Id).HasName("posts_pkey");

        entity.ToTable("posts");

        entity.HasIndex(e => e.PostBuilderId, "ix_posts_post_builder_id");
        entity.HasIndex(e => new { e.ChatSessionId, e.CreatedAt, e.Id }, "ix_posts_chat_session_created_at_id")
            .IsDescending(false, true, true);
        entity.HasIndex(e => new { e.UserId, e.WorkspaceId, e.CreatedAt }, "ix_posts_user_workspace_created_at")
            .IsDescending(false, false, true);

        entity.HasIndex(e => e.WorkspaceId, "ix_posts_workspace_id");
        entity.HasIndex(e => new { e.Status, e.ScheduledAtUtc }, "ix_posts_status_scheduled_at_utc");

        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.PostBuilderId).HasColumnName("post_builder_id");
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.Property(e => e.WorkspaceId).HasColumnName("workspace_id");
        entity.Property(e => e.ChatSessionId).HasColumnName("chat_session_id");
        entity.Property(e => e.SocialMediaId).HasColumnName("social_media_id");
        entity.Property(e => e.Platform).HasColumnName("platform");
        entity.Property(e => e.Title).HasColumnName("title");
        entity.Property(e => e.ScheduleGroupId).HasColumnName("schedule_group_id");
        entity.Property(e => e.RecommendPostId).HasColumnName("recommend_post_id");
        entity.HasIndex(e => e.RecommendPostId, "ux_posts_recommend_post_id")
            .IsUnique()
            .HasFilter("recommend_post_id IS NOT NULL");
        entity.Property(e => e.ScheduledSocialMediaIds)
            .HasColumnName("scheduled_social_media_ids")
            .HasColumnType("uuid[]");
        entity.Property(e => e.ScheduledIsPrivate).HasColumnName("scheduled_is_private");
        entity.Property(e => e.ScheduleTimezone).HasColumnName("schedule_timezone");
        entity.Property(e => e.ScheduledAtUtc).HasColumnName("scheduled_at_utc").HasColumnType("timestamp with time zone");

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

        entity.HasOne(e => e.PostBuilder)
            .WithMany(builder => builder.Posts)
            .HasForeignKey(e => e.PostBuilderId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("posts_post_builder_id_fkey");

        entity.HasOne(e => e.ChatSession)
            .WithMany()
            .HasForeignKey(e => e.ChatSessionId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("posts_chat_session_id_fkey");
    }

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

        if (!string.Equals(left.PostType, right.PostType, StringComparison.Ordinal))
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
        hash.Add(value.PostType);

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
            PostType = value.PostType,
            ResourceList = value.ResourceList is null ? null : new List<string>(value.ResourceList)
        };
    }
}
