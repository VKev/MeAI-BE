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
}

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
    IReadOnlyList<string>? Modes = null);

public sealed record RagVisualHit(
    string? DocumentId,
    string? Kind,
    string? Scope,
    string? ImageUrl,
    string? Caption,
    string? PostId,
    double Score);

public sealed record RagTextResults(
    string? Context,
    IReadOnlyList<string> MatchedDocumentIds);

public sealed record RagMultimodalQueryResponse(
    string Query,
    int TopK,
    string DocumentIdPrefix,
    RagTextResults? Text,
    IReadOnlyList<RagVisualHit>? Visual,
    string? VisualError);
