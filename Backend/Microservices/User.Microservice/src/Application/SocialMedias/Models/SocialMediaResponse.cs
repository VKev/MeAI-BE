using System.Text.Json;

namespace Application.SocialMedias.Models;

public sealed record SocialMediaResponse(
    Guid Id,
    string Type,
    JsonDocument? Metadata,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);
