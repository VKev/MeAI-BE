using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class UserSubscription
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid SubscriptionId { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? ActiveDate { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? EndDate { get; set; }

    public string? Status { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted { get; set; }
}
