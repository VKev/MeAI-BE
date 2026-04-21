using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
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

    [Column(TypeName = "integer")]
    public int DurationMonths { get; set; }

    [Column(TypeName = "numeric(18,2)")]
    public decimal? MeAiCoin { get; set; }

    public bool IsActive { get; set; } = true;

    [JsonIgnore]
    public string? StripeProductId { get; set; }

    [JsonIgnore]
    public string? StripePriceId { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [JsonIgnore]
    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }

    [JsonIgnore]
    public bool IsDeleted { get; set; }

}
