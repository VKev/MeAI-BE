using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class CoinPackage
{
    [Key]
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    [Column(TypeName = "numeric(18,2)")]
    public decimal CoinAmount { get; set; }

    [Column(TypeName = "numeric(18,2)")]
    public decimal BonusCoins { get; set; }

    [Column(TypeName = "numeric(18,2)")]
    public decimal Price { get; set; }

    public string Currency { get; set; } = "usd";

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }
}
