using System.Text.Json;

namespace Application.SocialMedias.Contracts;

public sealed record SocialMediaResponse(
    Guid Id,
    string Type,
    JsonDocument? Metadata,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);
