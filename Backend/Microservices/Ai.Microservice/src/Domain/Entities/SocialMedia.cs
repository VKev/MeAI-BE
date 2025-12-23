namespace Domain.Entities;

public sealed class SocialMedia
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string SocialMediaType { get; set; } = null!;

    public string? AccessToken { get; set; }

    public string? TokenType { get; set; }

    public string? RefreshToken { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
