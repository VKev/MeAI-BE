using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Domain.Entities;

[Index(nameof(UserId))]
public sealed class UserSubscription
{
    [Key] public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid SubscriptionId { get; set; }

    [Column(TypeName = "timestamp")]
    public DateTime? ActiveDate { get; set; }

    [Column(TypeName = "timestamp")]
    public DateTime? EndDate { get; set; }

    public string? Status { get; set; }

    [Column(TypeName = "timestamp")]
    public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "timestamp")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp")]
    public DateTime? DeletedAt { get; set; }
}
