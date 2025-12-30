using System.Text.Json.Serialization;

namespace Domain.Entities;

public sealed class PostContent
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("hashtag")]
    public string? Hashtag { get; set; }

    [JsonPropertyName("resource_list")]
    public List<string>? ResourceList { get; set; }
}
