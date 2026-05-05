using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

/// <summary>
/// Async "improve this existing post" recommendation. Mirrors <see cref="DraftPostTask"/>
/// but operates on an existing <see cref="Post"/> rather than creating a draft from
/// scratch. The pipeline is:
///   Step 0 — WaitForRagReady
///   Step 1 — re-index the social account so retrieval reflects current state
///   Step 2 — RAG multimodal query anchored on the original post (caption + images)
///   Step 3 — caption LLM with the "improve" system prompt (only if ImproveCaption)
///   Step 3.4 — fetch style-knowledge for the requested or inferred style
///   Step 4 — image-gen with the "improve" image-brief prompt (only if ImproveImage)
///   Step 5 — persist outputs on this row, mark Completed
/// The original Post is **never modified**. RecommendPost holds the suggested new
/// caption / image as separate fields. Replace-on-rerun is enforced at the start
/// command: any existing RecommendPost for the same OriginalPostId is hard-deleted
/// before the new row is inserted.
/// </summary>
public sealed class RecommendPost
{
    [Key]
    public Guid Id { get; set; }

    public Guid CorrelationId { get; set; }

    public Guid UserId { get; set; }

    public Guid? WorkspaceId { get; set; }

    /// <summary>
    /// FK to <see cref="Post"/>. Each Post has at most ONE active RecommendPost
    /// (enforced by a unique index — see <c>Post.RecommendPostId</c>). Replace-on-
    /// rerun is enforced at the command boundary, not by EF cascade.
    /// </summary>
    public Guid OriginalPostId { get; set; }

    /// <summary>True if Step 3 (caption regen) should run. At least one of
    /// <see cref="ImproveCaption"/> / <see cref="ImproveImage"/> must be true; the
    /// command rejects requests where both are false.</summary>
    public bool ImproveCaption { get; set; }

    /// <summary>True if Step 4 (image-gen) should run. See <see cref="ImproveCaption"/>.</summary>
    public bool ImproveImage { get; set; }

    /// <summary>
    /// Visual / copy style for the regen — same enum as <see cref="DraftPostStyles"/>.
    /// If the request omitted style, the start-command falls back to the original
    /// post's stored style and finally to <see cref="DraftPostStyles.Branded"/>.
    /// </summary>
    [MaxLength(32)]
    public string Style { get; set; } = DraftPostStyles.Branded;

    /// <summary>
    /// Optional free-form steering text from the user (e.g. "make the caption more
    /// playful", "use a cooler color palette in the image"). Forwarded verbatim into
    /// both the caption and image-brief LLM prompts when present.
    /// </summary>
    public string? UserInstruction { get; set; }

    public string Status { get; set; } = RecommendPostStatuses.Submitted;

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Suggested replacement caption (null when ImproveCaption=false or
    /// generation failed before this stage).</summary>
    public string? ResultCaption { get; set; }

    /// <summary>S3 resource id holding the suggested replacement image (null when
    /// ImproveImage=false or generation failed before this stage).</summary>
    public Guid? ResultResourceId { get; set; }

    /// <summary>Pre-signed URL of the suggested replacement image — convenience for
    /// the FE so it doesn't need a second round-trip.</summary>
    public string? ResultPresignedUrl { get; set; }

    [Column(TypeName = "jsonb")]
    public string? ResultReferencesJson { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? CompletedAt { get; set; }
}

public static class RecommendPostStatuses
{
    public const string Submitted = "Submitted";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}
