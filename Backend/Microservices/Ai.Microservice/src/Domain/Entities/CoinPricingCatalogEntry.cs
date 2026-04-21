using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

// One row per priced (ActionType, Model, Variant) AI action. Prices are stored in coin
// units — 1 coin = $0.01 USD per the product rate. Rows are user-visible for transparency
// (FE fetches the public catalog to show "costs N coins" on the Generate button) and
// editable by admins without a redeploy.
public sealed class CoinPricingCatalogEntry
{
    [Key]
    public Guid Id { get; set; }

    // "image_generation" | "image_reframe_variant" | "video_generation".
    public string ActionType { get; set; } = null!;

    // Kie model identifier — "nano-banana-pro" | "veo3_fast" | "veo3" | "veo3_quality".
    public string Model { get; set; } = null!;

    // Free-form tag to disambiguate within a model — "1K" / "2K" for image resolution,
    // "8s" for video duration, etc. Null means "default for this (ActionType, Model)".
    public string? Variant { get; set; }

    // Unit label for the UI: "per_image", "per_clip", "per_variant".
    public string Unit { get; set; } = null!;

    [Column(TypeName = "numeric(18,2)")]
    public decimal UnitCostCoins { get; set; }

    public bool IsActive { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }
}
