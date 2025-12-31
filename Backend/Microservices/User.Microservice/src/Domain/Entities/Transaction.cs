using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class Transaction
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? RelationId { get; set; }

    public string? RelationType { get; set; }

    [Column(TypeName = "numeric(18,2)")]
    public decimal? Cost { get; set; }

    public string? TransactionType { get; set; }

    public int? TokenUsed { get; set; }

    public string? PaymentMethod { get; set; }

    public string? Status { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted { get; set; }
}
