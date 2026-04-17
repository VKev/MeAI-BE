using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class Post
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string? Content { get; set; }

    public Guid[] ResourceIds { get; set; } = Array.Empty<Guid>();

    public string? MediaUrl { get; set; }

    public string? MediaType { get; set; } // "Image", "Video", null

    public int LikesCount { get; set; }

    public int CommentsCount { get; set; }

    public int SharesCount { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted { get; set; }
}
