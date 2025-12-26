using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Domain.Entities;

[Index(nameof(UserId))]
[Index(nameof(RelationId))]
public sealed class Transaction
{
    [Key] public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? RelationId { get; set; }

    public string? RelationType { get; set; }

    [Column(TypeName = "numeric(18,2)")]
    public decimal? Cost { get; set; }

    public string? TransactionType { get; set; }

    public int? TokenUsed { get; set; }

    public string? PaymentMethod { get; set; }

    public string? Status { get; set; }

    [Column(TypeName = "timestamp")]
    public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "timestamp")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp")]
    public DateTime? DeletedAt { get; set; }
}
