using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class User
{
    [Key]
    public Guid Id { get; set; }

    public string Username { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

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
    public decimal? MeAiCoin { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted { get; set; }
}
