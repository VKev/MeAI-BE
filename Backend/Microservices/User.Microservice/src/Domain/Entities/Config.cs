namespace Domain.Entities;

public sealed class Config
{
    public Guid Id { get; set; }

    public string? ChatModel { get; set; }

    public string? MediaAspectRatio { get; set; }

    public int? NumberOfVariances { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
