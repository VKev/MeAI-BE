using System.Text.Json.Serialization;

namespace Application.Abstractions.Meta;

public sealed class MetaDebugTokenResponse
{
    [JsonPropertyName("data")]
    public MetaDebugToken? Data { get; set; }
}

public sealed class MetaDebugToken
{
    [JsonPropertyName("is_valid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("app_id")]
    public string? AppId { get; set; }

    [JsonPropertyName("scopes")]
    public List<string>? Scopes { get; set; }

    [JsonPropertyName("granular_scopes")]
    public List<MetaGranularScope>? GranularScopes { get; set; }
}

public sealed class MetaGranularScope
{
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("target_ids")]
    public List<string>? TargetIds { get; set; }
}
