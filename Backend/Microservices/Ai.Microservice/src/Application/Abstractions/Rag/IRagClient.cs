namespace Application.Abstractions.Rag;

public interface IRagClient
{
    Task PublishIngestAsync(RagIngestMessage message, CancellationToken cancellationToken);

    Task PublishIngestBatchAsync(
        IReadOnlyCollection<RagIngestMessage> messages,
        CancellationToken cancellationToken);

    /// <summary>
    /// Synchronous batch ingest via gRPC. The call returns ONLY when every document
    /// has been embedded + upserted into Qdrant + the fingerprint registry. Use this
    /// when a downstream call (e.g. recommendation/draft query) needs the docs to
    /// be queryable immediately. For the fire-and-forget path use
    /// <see cref="PublishIngestBatchAsync"/> instead.
    ///
    /// May take seconds for text/image batches and minutes for video batches.
    /// </summary>
    Task<IReadOnlyList<RagIngestResult>> IngestBatchSyncAsync(
        IReadOnlyCollection<RagIngestMessage> messages,
        CancellationToken cancellationToken);

    Task<RagQueryResponse> QueryAsync(RagQueryRequest request, CancellationToken cancellationToken);

    Task<RagMultimodalQueryResponse> MultimodalQueryAsync(
        RagMultimodalQueryRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, string>> ListFingerprintsAsync(
        string documentIdPrefix,
        CancellationToken cancellationToken);

    /// <summary>
    /// Blocks until rag-microservice's lazy knowledge bootstrap is complete.
    /// Returns instantly once it's done. Callers that orchestrate downstream
    /// LLM/image-gen work (e.g. draft-post generation) should await this BEFORE
    /// the first real RAG call so the request never races a half-built index.
    /// Uses <see cref="RagOptions.WaitReadyTimeout"/> instead of the regular
    /// short RPC timeout, since cold bootstrap can take minutes.
    /// </summary>
    Task WaitForRagReadyAsync(CancellationToken cancellationToken);
}
