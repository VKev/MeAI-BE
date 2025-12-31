using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class RefreshToken
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = null!;

    public string? AccessTokenJti { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime ExpiresAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? RevokedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? AccessTokenRevokedAt { get; set; }
}
