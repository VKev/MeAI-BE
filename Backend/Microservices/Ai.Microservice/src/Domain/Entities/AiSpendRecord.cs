using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class AiSpendRecord
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? WorkspaceId { get; set; }

    public string Provider { get; set; } = "kie";

    public string ActionType { get; set; } = null!;

    public string Model { get; set; } = null!;

    public string? Variant { get; set; }

    public string Unit { get; set; } = null!;

    public int Quantity { get; set; }

    [Column(TypeName = "numeric(18,2)")]
    public decimal UnitCostCoins { get; set; }

    [Column(TypeName = "numeric(18,2)")]
    public decimal TotalCoins { get; set; }

    public string ReferenceType { get; set; } = null!;

    public string ReferenceId { get; set; } = null!;

    public string Status { get; set; } = "debited";

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }
}
