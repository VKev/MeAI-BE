using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class SocialAccountAnalysisSuggestion
{
    [Key]
    public Guid Id { get; set; }

    public Guid CorrelationId { get; set; }

    public Guid UserId { get; set; }

    public Guid SocialMediaId { get; set; }

    [MaxLength(32)]
    public string Platform { get; set; } = "unknown";

    [MaxLength(32)]
    public string Status { get; set; } = SocialAccountAnalysisSuggestionStatuses.Processing;

    public string? Suggestion { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    [Column(TypeName = "jsonb")]
    public string? RequestJson { get; set; }

    [Column(TypeName = "jsonb")]
    public string? ResponseJson { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CompletedAt { get; set; }
}

public static class SocialAccountAnalysisSuggestionStatuses
{
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}
