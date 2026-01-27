using System.Text.Json.Serialization;

namespace Infrastructure.TikTok;

// API Token Response
internal sealed class TikTokApiTokenResponse
{
    [JsonPropertyName("open_id")]
    public string? OpenId { get; set; }

    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_expires_in")]
    public int RefreshExpiresIn { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }
}

// Content Posting API request models
internal sealed class TikTokVideoPublishRequest
{
    [JsonPropertyName("post_info")]
    public TikTokApiPostInfo? PostInfo { get; set; }

    [JsonPropertyName("source_info")]
    public TikTokApiSourceInfo? SourceInfo { get; set; }
}

internal sealed class TikTokApiPostInfo
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("privacy_level")]
    public string PrivacyLevel { get; set; } = "SELF_ONLY";

    [JsonPropertyName("disable_duet")]
    public bool DisableDuet { get; set; }

    [JsonPropertyName("disable_comment")]
    public bool DisableComment { get; set; }

    [JsonPropertyName("disable_stitch")]
    public bool DisableStitch { get; set; }

    [JsonPropertyName("video_cover_timestamp_ms")]
    public int? VideoCoverTimestampMs { get; set; }
}

internal sealed class TikTokApiSourceInfo
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = "PULL_FROM_URL";

    [JsonPropertyName("video_url")]
    public string? VideoUrl { get; set; }

    [JsonPropertyName("video_size")]
    public long? VideoSize { get; set; }

    [JsonPropertyName("chunk_size")]
    public int? ChunkSize { get; set; }

    [JsonPropertyName("total_chunk_count")]
    public int? TotalChunkCount { get; set; }
}

// Content Posting API response models
internal sealed class TikTokApiVideoInitResponse
{
    [JsonPropertyName("data")]
    public TikTokApiVideoInitData? Data { get; set; }

    [JsonPropertyName("error")]
    public TikTokApiError? Error { get; set; }
}

internal sealed class TikTokApiVideoInitData
{
    [JsonPropertyName("publish_id")]
    public string? PublishId { get; set; }

    [JsonPropertyName("upload_url")]
    public string? UploadUrl { get; set; }
}

internal sealed class TikTokApiPublishStatusResponse
{
    [JsonPropertyName("data")]
    public TikTokApiPublishStatusData? Data { get; set; }

    [JsonPropertyName("error")]
    public TikTokApiError? Error { get; set; }
}

internal sealed class TikTokApiPublishStatusData
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("publicly_available_post_id")]
    public List<string>? PubliclyAvailablePostId { get; set; }

    [JsonPropertyName("fail_reason")]
    public string? FailReason { get; set; }
}

internal sealed class TikTokApiError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("log_id")]
    public string? LogId { get; set; }
}
