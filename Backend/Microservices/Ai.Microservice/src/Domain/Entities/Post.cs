using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class Post
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? SocialMediaId { get; set; }

    public string? Title { get; set; }

    [Column(TypeName = "jsonb")]
    public PostContent? Content { get; set; }

    public string? Status { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }
}
