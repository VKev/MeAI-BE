namespace Application.Abstractions.Rag;

public interface IMultimodalLlmClient
{
    Task<MultimodalAnswerResult> GenerateAnswerAsync(
        MultimodalAnswerRequest request,
        CancellationToken cancellationToken);
}

public sealed record MultimodalAnswerRequest(
    string SystemPrompt,
    string UserText,
    IReadOnlyList<string>? ReferenceImageUrls);

public sealed record MultimodalAnswerResult(
    string Answer,
    IReadOnlyList<WebSource> Sources);

/// <summary>
/// One web source surfaced from the answer-generation LLM call.
/// Two ways one of these gets created:
///   1. Function-calling tool path — the model invoked `web_search`, runtime hit
///      Brave Search, top results were attached to the response. <see cref="Snippet"/>
///      carries Brave's description; offsets are null because the model didn't
///      explicitly cite character spans.
///   2. Server-side search-preview / `plugins[web]` path — the model emitted
///      `annotations[].url_citation` with character offsets. <see cref="StartIndex"/>
///      and <see cref="EndIndex"/> are populated; Snippet may be null.
/// Frontend rendering: if offsets present, splice as inline link; else show as a
/// "Sources" footer block.
/// </summary>
public sealed record WebSource(
    string Url,
    string? Title,
    string? Snippet = null,
    int? StartIndex = null,
    int? EndIndex = null);
