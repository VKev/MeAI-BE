using System.Text.Json;

namespace Application.Posts;

internal static class SocialMediaMetadataHelper
{
    public static JsonDocument? Parse(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(metadataJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? GetString(JsonDocument? metadata, string propertyName)
    {
        if (metadata == null)
        {
            return null;
        }

        if (!metadata.RootElement.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    public static bool HasScope(JsonDocument? metadata, string scope)
    {
        var scopeValue = GetString(metadata, "scope");
        if (string.IsNullOrWhiteSpace(scopeValue))
        {
            return false;
        }

        return scopeValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => string.Equals(item, scope, StringComparison.OrdinalIgnoreCase));
    }
}
