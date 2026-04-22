using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class CoinTransaction
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    // Positive = credit (grant, refund). Negative = debit (AI spend).
    [Column(TypeName = "numeric(18,2)")]
    public decimal Delta { get; set; }

    // e.g. "ai.image_generation.debit", "ai.image_generation.refund",
    //       "ai.video_generation.debit", "ai.video_generation.refund",
    //       "subscription.grant", "admin.adjustment".
    public string Reason { get; set; } = null!;

    // "chat_image" / "chat_video" / "subscription" / "admin" — whatever this entry ties back to.
    public string? ReferenceType { get; set; }

    // Free-form id pointing at the source (Chat.Id, Subscription.Id, KIE correlation id).
    public string? ReferenceId { get; set; }

    // Balance after this entry was applied — cached so read-side queries don't have to
    // SUM the whole ledger.
    [Column(TypeName = "numeric(18,2)")]
    public decimal BalanceAfter { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }
}
