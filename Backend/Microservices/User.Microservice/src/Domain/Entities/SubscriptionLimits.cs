using System.Text.Json.Serialization;

namespace Domain.Entities;

public sealed class SubscriptionLimits
{
    [JsonPropertyName("number_of_social_accounts")]
    public int? NumberOfSocialAccounts { get; set; }

    [JsonPropertyName("rate_limit_for_content_creation")]
    public int? RateLimitForContentCreation { get; set; }

    [JsonPropertyName("number_of_workspaces")]
    public int? NumberOfWorkspaces { get; set; }
}
