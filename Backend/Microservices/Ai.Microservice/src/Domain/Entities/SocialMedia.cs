using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class SocialMedia
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string SocialMediaType { get; set; } = null!;

    public string? AccessToken { get; set; }

    public string? TokenType { get; set; }

    public string? RefreshToken { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? ExpiresAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }
}
