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

    /// <summary>
    /// True when the user did not supply a topic (the "lazy user" flow). The consumer
    /// will then auto-discover the next post topic by RAG'ing the page profile +
    /// past posts and web-searching for what's currently trending in those pillars.
    /// <see cref="UserPrompt"/> will hold a human-readable placeholder marker.
    /// </summary>
    public bool IsAutoTopic { get; set; }

    /// <summary>
    /// Visual / copy style for the generated post — drives which design rules the
    /// image-brief LLM RAGs from the knowledge base AND how aggressively the caption
    /// surfaces brand contact info. Defaults to <see cref="DraftPostStyles.Branded"/>.
    /// </summary>
    [MaxLength(32)]
    public string Style { get; set; } = DraftPostStyles.Branded;

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

/// <summary>
/// Allowed visual / copy styles for AI-generated draft posts. Each style maps
/// 1:1 to a knowledge-base namespace under <c>knowledge:image-design-{style}:</c>
/// from which the image-brief LLM pulls design rules.
/// </summary>
public static class DraftPostStyles
{
    /// <summary>Pure visual / mood / no on-image text. Editorial / lifestyle.</summary>
    public const string Creative = "creative";

    /// <summary>Default. Hero visual + subtle brand mark + optional short headline.
    /// Best for everyday content (tips, news, storytelling, engagement).</summary>
    public const string Branded = "branded";

    /// <summary>Full promo: brand logo + headline + value prop + CTA + contact line
    /// rendered on the image. Caption also pushes contact info aggressively.</summary>
    public const string Marketing = "marketing";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Creative, Branded, Marketing };

    /// <summary>
    /// Lenient: null/empty falls back to <see cref="Branded"/>; unknown values also fall
    /// back to Branded. Used by the consumer (defensive) for messages that may have been
    /// queued before this field existed. New API requests should use <see cref="TryValidate"/>
    /// at the command-handler boundary so unknown values surface as a 400.
    /// </summary>
    public static string NormalizeOrDefault(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Branded;
        var lower = raw.Trim().ToLowerInvariant();
        return All.Contains(lower) ? lower : Branded;
    }

    /// <summary>
    /// Strict validation for API input. Contract:
    ///   - <c>null</c> or whitespace → returns <c>true</c> with <paramref name="normalized"/> = <see cref="Branded"/> (default)
    ///   - One of the allowed styles (case-insensitive) → returns <c>true</c> with the lower-case form
    ///   - Anything else → returns <c>false</c>; caller must reject the request
    /// </summary>
    public static bool TryValidate(string? raw, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            normalized = Branded;
            return true;
        }
        var lower = raw.Trim().ToLowerInvariant();
        if (All.Contains(lower))
        {
            normalized = lower;
            return true;
        }
        normalized = Branded;
        return false;
    }
}
