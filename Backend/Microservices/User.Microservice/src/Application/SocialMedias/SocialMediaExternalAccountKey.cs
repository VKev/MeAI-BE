using System.Text.Json;
using Domain.Entities;

namespace Application.SocialMedias;

public static class SocialMediaExternalAccountKey
{
    public static string Resolve(SocialMedia socialMedia)
    {
        var platform = socialMedia.Type.Trim().ToLowerInvariant();
        var externalId = ResolveExternalId(platform, socialMedia.Metadata);
        return string.IsNullOrWhiteSpace(externalId)
            ? $"{platform}:connection:{socialMedia.Id:N}"
            : $"{platform}:account:{externalId}";
    }

    private static string? ResolveExternalId(string platform, JsonDocument? metadata)
    {
        if (metadata == null)
        {
            return null;
        }

        var properties = platform switch
        {
            "facebook" => new[] { "page_id", "id" },
            "instagram" => new[] { "instagram_business_account_id", "user_id", "id" },
            "tiktok" => new[] { "open_id", "user_id", "id" },
            "threads" => new[] { "user_id", "id" },
            _ => new[] { "id", "user_id" }
        };

        foreach (var propertyName in properties)
        {
            if (metadata.RootElement.TryGetProperty(propertyName, out var property) &&
                property.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(property.GetString()))
            {
                return property.GetString()!.Trim();
            }
        }

        return null;
    }
}
