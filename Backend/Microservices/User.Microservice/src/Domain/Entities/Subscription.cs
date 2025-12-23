namespace Domain.Entities;

public sealed class Subscription
{
    public Guid Id { get; set; }

    public string? Name { get; set; }

    public int? NumberOfSocialAccounts { get; set; }

    public decimal? MeAiCoin { get; set; }

    public int? RateLimitForContentCreation { get; set; }

    public int? NumberOfWorkspaces { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public ICollection<UserSubscription> UserSubscriptions { get; set; } = new List<UserSubscription>();
}
