using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Domain.Entities;

[Index(nameof(Username), IsUnique = true)]
[Index(nameof(Email), IsUnique = true)]
public sealed class User
{
    [Key] public Guid Id { get; set; }

    [Required]
    public string Username { get; set; } = null!;

    [Required]
    public string PasswordHash { get; set; } = null!;

    [Required]
    public string Email { get; set; } = null!;

    public bool EmailVerified { get; set; }

    public string? FullName { get; set; }

    [Column(TypeName = "date")]
    public DateTime? Birthday { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Provider { get; set; }

    public Guid? AvatarResourceId { get; set; }

    public string? Address { get; set; }

    [Column(TypeName = "numeric(18,2)")]
    public decimal? MeAiCoin { get; set; } = 0m;

    [Column(TypeName = "timestamp")]
    public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "timestamp")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp")]
    public DateTime? DeletedAt { get; set; }
}
