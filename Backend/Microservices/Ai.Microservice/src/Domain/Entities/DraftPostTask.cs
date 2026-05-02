using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

public sealed class DraftPostTask
{
    [Key]
    public Guid Id { get; set; }

    public Guid CorrelationId { get; set; }

    public Guid UserId { get; set; }

    public Guid SocialMediaId { get; set; }

    public Guid? WorkspaceId { get; set; }

    public string UserPrompt { get; set; } = string.Empty;

    public int TopK { get; set; }

    public int MaxReferenceImages { get; set; }

    public int MaxRagPosts { get; set; }

    public string Status { get; set; } = DraftPostTaskStatuses.Submitted;

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public Guid? ResultPostBuilderId { get; set; }

    public Guid? ResultPostId { get; set; }

    public Guid? ResultResourceId { get; set; }

    public string? ResultPresignedUrl { get; set; }

    public string? ResultCaption { get; set; }

    [Column(TypeName = "jsonb")]
    public string? ResultReferencesJson { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CompletedAt { get; set; }
}

public static class DraftPostTaskStatuses
{
    public const string Submitted = "Submitted";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}
