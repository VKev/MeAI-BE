namespace Application.Abstractions.Rag;

public interface IRagClient
{
    Task PublishIngestAsync(RagIngestMessage message, CancellationToken cancellationToken);

    Task PublishIngestBatchAsync(
        IReadOnlyCollection<RagIngestMessage> messages,
        CancellationToken cancellationToken);

    Task<RagQueryResponse> QueryAsync(RagQueryRequest request, CancellationToken cancellationToken);

    Task<RagMultimodalQueryResponse> MultimodalQueryAsync(
        RagMultimodalQueryRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, string>> ListFingerprintsAsync(
        string documentIdPrefix,
        CancellationToken cancellationToken);
}
