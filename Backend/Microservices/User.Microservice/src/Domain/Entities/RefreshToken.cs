using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Domain.Entities;

[Index(nameof(UserId))]
[Index(nameof(TokenHash), IsUnique = true)]
[Index(nameof(AccessTokenJti), IsUnique = true)]
public sealed class RefreshToken
{
    [Key] public Guid Id { get; set; }

    public Guid UserId { get; set; }

    [Required]
    public string TokenHash { get; set; } = null!;

    public string? AccessTokenJti { get; set; }

    [Column(TypeName = "timestamptz")]
    public DateTime ExpiresAt { get; set; }

    [Column(TypeName = "timestamptz")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "timestamptz")]
    public DateTime? RevokedAt { get; set; }

    [Column(TypeName = "timestamptz")]
    public DateTime? AccessTokenRevokedAt { get; set; }
}
