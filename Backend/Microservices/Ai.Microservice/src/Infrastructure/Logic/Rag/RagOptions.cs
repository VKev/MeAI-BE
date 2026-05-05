namespace Infrastructure.Logic.Rag;

public sealed class RagOptions
{
    public string IngestQueue { get; init; } = "meai.rag.ingest";
    public string QueryQueue { get; init; } = "meai.rag.query";
    public TimeSpan RpcTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// gRPC endpoint of rag-microservice for synchronous batch ingest. The
    /// AMQP-RPC query path uses RabbitMQ; the synchronous-ingest path uses gRPC
    /// because we need the call to block until all docs are queryable.
    /// </summary>
    public string GrpcUrl { get; init; } = "http://rag-microservice:5006";

    /// <summary>
    /// Per-call deadline for synchronous gRPC ingest. Big enough to accommodate
    /// LightRAG entity extraction + embedding for a batch of ~10 docs.
    /// </summary>
    public TimeSpan GrpcIngestTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Timeout for the dedicated <c>WaitForRagReady</c> RPC. Cold-container
    /// knowledge bootstrap (LightRAG entity extraction across ~25-80 docs) can
    /// take 1-3 minutes; this gives generous headroom while still bounding any
    /// pathological hang. After bootstrap completes once, the call returns
    /// instantly for the rest of the container's lifetime.
    /// </summary>
    public TimeSpan WaitReadyTimeout { get; init; } = TimeSpan.FromMinutes(30);
}
