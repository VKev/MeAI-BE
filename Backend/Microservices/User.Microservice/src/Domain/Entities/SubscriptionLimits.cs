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

    [JsonPropertyName("max_pages_per_social_account")]
    public int? MaxPagesPerSocialAccount { get; set; }

    [JsonPropertyName("storage_quota_bytes")]
    public long? StorageQuotaBytes { get; set; }

    [JsonPropertyName("max_upload_file_bytes")]
    public long? MaxUploadFileBytes { get; set; }

    [JsonPropertyName("retention_days_after_delete")]
    public int? RetentionDaysAfterDelete { get; set; }
}
