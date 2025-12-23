namespace Domain.Entities;

public sealed class Transaction
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? RelationId { get; set; }

    public string? RelationType { get; set; }

    public decimal? Cost { get; set; }

    public string? TransactionType { get; set; }

    public int? TokenUsed { get; set; }

    public string? PaymentMethod { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public User User { get; set; } = null!;
}
