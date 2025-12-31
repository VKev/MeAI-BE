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
