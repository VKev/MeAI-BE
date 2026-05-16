using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Rag;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SharedLibrary.Configs;
using SharedLibrary.Grpc.Rag;

namespace Infrastructure.Logic.Rag;

public sealed class RabbitMqRagClient : IRagClient, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly RagOptions _options;
    private readonly EnvironmentConfig _env;
    private readonly RagIngestService.RagIngestServiceClient _grpcIngest;
    private readonly ILogger<RabbitMqRagClient> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;
    private bool _disposed;

    public RabbitMqRagClient(
        RagOptions options,
        EnvironmentConfig env,
        RagIngestService.RagIngestServiceClient grpcIngest,
        ILogger<RabbitMqRagClient> logger)
    {
        _options = options;
        _env = env;
        _grpcIngest = grpcIngest;
        _logger = logger;
    }

    public async Task PublishIngestAsync(RagIngestMessage message, CancellationToken cancellationToken)
    {
        await PublishIngestBatchAsync(new[] { message }, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishIngestBatchAsync(
        IReadOnlyCollection<RagIngestMessage> messages,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return;
        }

        await using var channel = await CreateChannelAsync(cancellationToken).ConfigureAwait(false);

        await channel.QueueDeclareAsync(
            queue: _options.IngestQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var props = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
        };

        foreach (var message in messages)
        {
            var body = JsonSerializer.SerializeToUtf8Bytes((object)message, JsonOptions);
            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: _options.IngestQueue,
                mandatory: false,
                basicProperties: props,
                body: body,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<RagIngestResult>> IngestBatchSyncAsync(
        IReadOnlyCollection<RagIngestMessage> messages,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return Array.Empty<RagIngestResult>();
        }

        var request = new IngestBatchRequest();
        foreach (var m in messages)
        {
            request.Documents.Add(new IngestDocument
            {
                Kind = m.Kind ?? string.Empty,
                DocumentId = m.DocumentId ?? string.Empty,
                Fingerprint = m.Fingerprint ?? string.Empty,
                Content = m.Content ?? string.Empty,
                ImageUrl = m.ImageUrl ?? string.Empty,
                Caption = m.Caption ?? string.Empty,
                DescribePrompt = m.DescribePrompt ?? string.Empty,
                Scope = m.Scope ?? string.Empty,
                PostId = m.PostId ?? string.Empty,
                Platform = m.Platform ?? string.Empty,
                SocialMediaId = m.SocialMediaId ?? string.Empty,
                VideoUrl = m.VideoUrl ?? string.Empty,
            });
        }

        var deadline = DateTime.UtcNow.Add(_options.GrpcIngestTimeout);
        _logger.LogInformation(
            "RAG gRPC IngestBatch starting: {DocCount} docs, deadline={DeadlineSec}s",
            request.Documents.Count, _options.GrpcIngestTimeout.TotalSeconds);

        IngestBatchResponse response;
        try
        {
            response = await _grpcIngest
                .IngestBatchAsync(request, deadline: deadline, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Grpc.Core.RpcException ex)
        {
            _logger.LogError(
                ex,
                "RAG gRPC IngestBatch failed: status={Status} detail={Detail}",
                ex.StatusCode, ex.Status.Detail);
            throw new InvalidOperationException(
                $"RAG synchronous ingest failed: {ex.StatusCode} {ex.Status.Detail}", ex);
        }

        var results = new List<RagIngestResult>(response.Results.Count);
        var failed = 0;
        foreach (var r in response.Results)
        {
            if (string.Equals(r.Status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                failed++;
                _logger.LogWarning(
                    "RAG ingest doc failed: docId={DocId} error={Error}", r.DocumentId, r.Error);
            }
            results.Add(new RagIngestResult(r.DocumentId, r.Status, r.Fingerprint,
                string.IsNullOrEmpty(r.Error) ? null : r.Error));
        }

        _logger.LogInformation(
            "RAG gRPC IngestBatch done: {Total} docs, {Failed} failed",
            results.Count, failed);

        return results;
    }

    public async Task<RagQueryResponse> QueryAsync(RagQueryRequest request, CancellationToken cancellationToken)
    {
        var raw = await SendRpcAsync(request, cancellationToken).ConfigureAwait(false);
        var response = JsonSerializer.Deserialize<RagQueryResponse>(raw, JsonOptions);
        if (response == null)
        {
            throw new InvalidOperationException("RAG query returned an empty payload.");
        }
        return response;
    }

    public async Task<IReadOnlyDictionary<string, string>> ListFingerprintsAsync(
        string documentIdPrefix,
        CancellationToken cancellationToken)
    {
        var payload = new ListFingerprintsRequest(documentIdPrefix);
        var raw = await SendRpcAsync(payload, cancellationToken).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("fingerprints", out var fps) ||
            fps.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(0);
        }

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in fps.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.GetString() ?? string.Empty;
        }
        return dict;
    }

    public async Task<RagMultimodalQueryResponse> MultimodalQueryAsync(
        RagMultimodalQueryRequest request,
        CancellationToken cancellationToken)
    {
        var payload = new MultimodalQueryRequest(
            Query: request.Query,
            DocumentIdPrefix: request.DocumentIdPrefix,
            TopK: request.TopK,
            Modes: request.Modes ?? new[] { "text", "visual" },
            Platform: request.Platform,
            SocialMediaId: request.SocialMediaId);

        var raw = await SendRpcAsync(payload, cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var err))
        {
            throw new InvalidOperationException(
                $"RAG multimodal_query returned error: {err.GetString()}");
        }

        RagTextResults? textResults = null;
        if (root.TryGetProperty("text", out var textNode) && textNode.ValueKind == JsonValueKind.Object)
        {
            var ctx = textNode.TryGetProperty("context", out var ctxNode) && ctxNode.ValueKind == JsonValueKind.String
                ? ctxNode.GetString()
                : null;
            var matched = new List<string>();
            if (textNode.TryGetProperty("matchedDocumentIds", out var midsNode) && midsNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in midsNode.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String) matched.Add(item.GetString()!);
                }
            }
            textResults = new RagTextResults(ctx, matched);
        }

        var visualHits = new List<RagVisualHit>();
        if (root.TryGetProperty("visual", out var visualNode) && visualNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var hit in visualNode.EnumerateArray())
            {
                visualHits.Add(new RagVisualHit(
                    DocumentId: ReadString(hit, "documentId"),
                    Kind: ReadString(hit, "kind"),
                    Scope: ReadString(hit, "scope"),
                    ImageUrl: ReadString(hit, "imageUrl"),
                    Caption: ReadString(hit, "caption"),
                    PostId: ReadString(hit, "postId"),
                    Score: hit.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number
                        ? sc.GetDouble()
                        : 0d,
                    MirroredImageUrl: NormalizeS3PublicUrl(ReadString(hit, "mirroredImageUrl"))));
            }
        }

        var visualError = root.TryGetProperty("visualError", out var veNode) && veNode.ValueKind == JsonValueKind.String
            ? veNode.GetString()
            : null;

        var videoHits = new List<RagVideoSegmentHit>();
        if (root.TryGetProperty("video", out var videoNode) && videoNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var hit in videoNode.EnumerateArray())
            {
                videoHits.Add(new RagVideoSegmentHit(
                    VideoName: ReadString(hit, "videoName") ?? ReadString(hit, "__video_name__"),
                    PostId: ReadString(hit, "postId"),
                    Index: ReadString(hit, "index") ?? ReadString(hit, "__index__"),
                    Time: ReadString(hit, "time"),
                    Caption: ReadString(hit, "caption"),
                    Transcript: ReadString(hit, "transcript"),
                    Score: hit.TryGetProperty("distance", out var dist) && dist.ValueKind == JsonValueKind.Number
                        ? dist.GetDouble()
                        : (hit.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetDouble() : 0d),
                    // Frame-level fields — populated when the segment store has per-frame
                    // vectors (frame_url is the S3 URL of the highest-scoring sampled frame).
                    FrameUrl: NormalizeS3PublicUrl(ReadString(hit, "frameUrl") ?? ReadString(hit, "frame_url")),
                    FrameIndex: hit.TryGetProperty("frameIndex", out var fi) && fi.ValueKind == JsonValueKind.Number
                        ? fi.GetInt32()
                        : (hit.TryGetProperty("frame_index", out var fi2) && fi2.ValueKind == JsonValueKind.Number ? fi2.GetInt32() : (int?)null)));
            }
        }

        var videoError = root.TryGetProperty("videoError", out var vidErrNode) && vidErrNode.ValueKind == JsonValueKind.String
            ? vidErrNode.GetString()
            : null;

        return new RagMultimodalQueryResponse(
            Query: ReadString(root, "query") ?? request.Query,
            TopK: root.TryGetProperty("topK", out var tk) && tk.ValueKind == JsonValueKind.Number ? tk.GetInt32() : request.TopK,
            DocumentIdPrefix: ReadString(root, "documentIdPrefix") ?? request.DocumentIdPrefix,
            Text: textResults,
            Visual: visualHits,
            VisualError: visualError,
            Video: videoHits,
            VideoError: videoError);
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }
        return null;
    }

    private string? NormalizeS3PublicUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl) ||
            string.IsNullOrWhiteSpace(_options.S3PublicBaseUrl) ||
            !Uri.TryCreate(rawUrl, UriKind.Absolute, out var source) ||
            !IsS3Host(source.Host) ||
            !Uri.TryCreate(_options.S3PublicBaseUrl, UriKind.Absolute, out var publicBase))
        {
            return rawUrl;
        }

        var builder = new UriBuilder(source)
        {
            Scheme = publicBase.Scheme,
            Host = publicBase.Host,
            Port = publicBase.IsDefaultPort ? -1 : publicBase.Port,
        };

        var basePath = publicBase.AbsolutePath.Trim('/');
        if (!string.IsNullOrWhiteSpace(basePath))
        {
            builder.Path = $"{basePath}/{source.AbsolutePath.TrimStart('/')}";
        }

        return builder.Uri.ToString();
    }

    private static bool IsS3Host(string host)
    {
        return host.Equals("s3.amazonaws.com", StringComparison.OrdinalIgnoreCase) ||
               (host.StartsWith("s3.", StringComparison.OrdinalIgnoreCase) &&
                host.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase)) ||
               host.EndsWith(".s3.amazonaws.com", StringComparison.OrdinalIgnoreCase) ||
               (host.Contains(".s3.", StringComparison.OrdinalIgnoreCase) &&
                host.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase));
    }

    public async Task WaitForRagReadyAsync(CancellationToken cancellationToken)
    {
        var payload = new { op = "wait_ready" };
        _logger.LogInformation(
            "RAG WaitForReady: awaiting rag-microservice knowledge bootstrap (timeout={Sec}s)",
            _options.WaitReadyTimeout.TotalSeconds);
        var raw = await SendRpcAsync(payload, cancellationToken, _options.WaitReadyTimeout)
            .ConfigureAwait(false);
        using var doc = JsonDocument.Parse(raw);
        var ready = doc.RootElement.TryGetProperty("ready", out var r) && r.ValueKind == JsonValueKind.True;
        if (!ready)
        {
            throw new InvalidOperationException(
                "RAG WaitForReady returned non-ready payload: "
                + System.Text.Encoding.UTF8.GetString(raw));
        }
        _logger.LogInformation("RAG WaitForReady: ready");
    }

    private async Task<byte[]> SendRpcAsync(
        object payload,
        CancellationToken cancellationToken,
        TimeSpan? timeoutOverride = null)
    {
        await using var channel = await CreateChannelAsync(cancellationToken).ConfigureAwait(false);

        await channel.QueueDeclareAsync(
            queue: _options.QueryQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var replyQueue = await channel.QueueDeclareAsync(
            queue: string.Empty,
            durable: false,
            exclusive: true,
            autoDelete: true,
            arguments: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var correlationId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) =>
        {
            if (string.Equals(ea.BasicProperties.CorrelationId, correlationId, StringComparison.Ordinal))
            {
                tcs.TrySetResult(ea.Body.ToArray());
            }
            return Task.CompletedTask;
        };

        await channel.BasicConsumeAsync(
            queue: replyQueue.QueueName,
            autoAck: true,
            consumer: consumer,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var body = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var props = new BasicProperties
        {
            ContentType = "application/json",
            CorrelationId = correlationId,
            ReplyTo = replyQueue.QueueName,
        };

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: _options.QueryQueue,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var effectiveTimeout = timeoutOverride ?? _options.RpcTimeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(effectiveTimeout);

        await using var registration = timeoutCts.Token.Register(() =>
            tcs.TrySetException(new TimeoutException(
                $"RAG RPC timed out after {effectiveTimeout.TotalSeconds:F0}s on queue '{_options.QueryQueue}'.")));

        return await tcs.Task.ConfigureAwait(false);
    }

    private async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            if (_connection is not null)
            {
                try { await _connection.DisposeAsync().ConfigureAwait(false); } catch { /* ignore */ }
                _connection = null;
            }

            var factory = new ConnectionFactory();
            if (_env.IsRabbitMqCloud && !string.IsNullOrEmpty(_env.RabbitMqUrl))
            {
                factory.Uri = new Uri(_env.RabbitMqUrl);
            }
            else
            {
                factory.HostName = _env.RabbitMqHost;
                factory.Port = _env.RabbitMqPort;
                factory.UserName = _env.RabbitMqUser;
                factory.Password = _env.RabbitMqPassword;
            }

            _logger.LogDebug(
                "Opening RabbitMQ connection for RAG client (host={Host} cloud={Cloud})",
                _env.RabbitMqHost,
                _env.IsRabbitMqCloud);

            _connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_connection is not null)
        {
            try { await _connection.CloseAsync(); } catch { /* ignore */ }
            try { await _connection.DisposeAsync(); } catch { /* ignore */ }
            _connection = null;
        }
        _connectionLock.Dispose();
    }

    private sealed record ListFingerprintsRequest(
        [property: JsonPropertyName("documentIdPrefix")] string DocumentIdPrefix)
    {
        [JsonPropertyName("op")]
        public string Op => "list_fingerprints";
    }

    private sealed record MultimodalQueryRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("documentIdPrefix")] string DocumentIdPrefix,
        [property: JsonPropertyName("topK")] int TopK,
        [property: JsonPropertyName("modes")] IReadOnlyList<string> Modes,
        [property: JsonPropertyName("platform")] string? Platform,
        [property: JsonPropertyName("socialMediaId")] string? SocialMediaId)
    {
        [JsonPropertyName("op")]
        public string Op => "multimodal_query";
    }
}
