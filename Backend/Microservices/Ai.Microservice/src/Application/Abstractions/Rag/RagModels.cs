namespace Application.Abstractions.Rag;

public sealed record RagIngestMessage
{
    public required string Kind { get; init; }
    public required string DocumentId { get; init; }
    public required string Fingerprint { get; init; }
    public string? Content { get; init; }
    public string? Caption { get; init; }
    public string? ImageUrl { get; init; }
    public string? ImageBase64 { get; init; }
    public string? MimeType { get; init; }
    public string? DescribePrompt { get; init; }
    public string? Scope { get; init; }
    public string? PostId { get; init; }
    // VideoRAG (kind="video"): per-account video ingest. Platform + SocialMediaId
    // are required so the Rag microservice can scope the per-account VideoRAG instance.
    public string? Platform { get; init; }
    public string? SocialMediaId { get; init; }
    public string? VideoUrl { get; init; }
}

/// <summary>
/// Per-document result from <see cref="IRagClient.IngestBatchSyncAsync"/>.
/// </summary>
public sealed record RagIngestResult(
    string DocumentId,
    /// <summary>"ingested" | "updated" | "unchanged" | "failed"</summary>
    string Status,
    string Fingerprint,
    /// <summary>Populated only when <see cref="Status"/> = "failed".</summary>
    string? Error = null);

public sealed record RagQueryRequest(
    string Query,
    string? DocumentIdPrefix = null,
    string Mode = "hybrid",
    int TopK = 10,
    bool OnlyNeedContext = false);

public sealed record RagQueryResponse(
    string Query,
    string Mode,
    int TopK,
    string? Answer,
    IReadOnlyList<string>? MatchedDocumentIds);

public sealed record RagMultimodalQueryRequest(
    string Query,
    string DocumentIdPrefix,
    int TopK = 8,
    IReadOnlyList<string>? Modes = null,
    string? Platform = null,
    string? SocialMediaId = null);

public sealed record RagVisualHit(
    string? DocumentId,
    string? Kind,
    string? Scope,
    string? ImageUrl,
    string? Caption,
    string? PostId,
    double Score,
    /// <summary>
    /// Fresh presigned S3 URL of the image bytes, mirrored from <see cref="ImageUrl"/>
    /// at ingest time. OpenAI / OpenRouter can fetch this; the original FB-CDN
    /// <see cref="ImageUrl"/> they cannot. Prefer this when handing image refs to
    /// the multimodal LLM. Null if the visual point was ingested before the
    /// S3-mirror was wired in (re-index to populate).
    /// </summary>
    string? MirroredImageUrl = null);

public sealed record RagVideoSegmentHit(
    string? VideoName,
    string? PostId,
    string? Index,
    string? Time,
    string? Caption,
    string? Transcript,
    double Score,
    /// <summary>
    /// S3 URL of the highest-scoring sampled frame within this segment, or null
    /// if frame-level vectors aren't available (e.g. legacy segment-level rows).
    /// Surfaced into the image-rerank pool so image-gen can use the actual
    /// visually-matching frame as a reference instead of just the static thumbnail.
    /// </summary>
    string? FrameUrl = null,
    /// <summary>0-based frame index within the segment (typically 0..4 for 5 frames).</summary>
    int? FrameIndex = null);

public sealed record RagTextResults(
    string? Context,
    IReadOnlyList<string> MatchedDocumentIds);

public sealed record RagMultimodalQueryResponse(
    string Query,
    int TopK,
    string DocumentIdPrefix,
    RagTextResults? Text,
    IReadOnlyList<RagVisualHit>? Visual,
    string? VisualError,
    IReadOnlyList<RagVideoSegmentHit>? Video = null,
    string? VideoError = null);
