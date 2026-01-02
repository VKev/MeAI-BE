using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace Domain.Entities;

public sealed class Subscription
{
    [Key]
    public Guid Id { get; set; }

    public string? Name { get; set; }

    [Column(TypeName = "jsonb")]
    public SubscriptionLimits? Limits { get; set; }

    [Column(TypeName = "real")]
    public float? Cost { get; set; }

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
