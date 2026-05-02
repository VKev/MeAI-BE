namespace Application.Abstractions.Rag;

public interface IMultimodalLlmClient
{
    Task<string> GenerateAnswerAsync(
        MultimodalAnswerRequest request,
        CancellationToken cancellationToken);
}

public sealed record MultimodalAnswerRequest(
    string SystemPrompt,
    string UserText,
    IReadOnlyList<string>? ReferenceImageUrls);
