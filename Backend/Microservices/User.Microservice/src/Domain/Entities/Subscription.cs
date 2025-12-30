using System.Text.Json.Serialization;

namespace Domain.Entities;

public sealed class Subscription
{
    public Guid Id { get; set; }

    public string? Name { get; set; }

    public SubscriptionLimits? Limits { get; set; }

    public decimal? MeAiCoin { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    [JsonIgnore]
    public ICollection<UserSubscription> UserSubscriptions { get; set; } = new List<UserSubscription>();
}
