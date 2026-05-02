namespace Infrastructure.Logic.Rag;

public sealed class RagOptions
{
    public string IngestQueue { get; init; } = "meai.rag.ingest";
    public string QueryQueue { get; init; } = "meai.rag.query";
    public TimeSpan RpcTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
