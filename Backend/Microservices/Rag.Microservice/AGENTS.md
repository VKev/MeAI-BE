# AGENTS.md — Rag.Microservice

## What this service is

A **RabbitMQ-driven RAG (retrieval-augmented generation) sidecar** that the Ai.Microservice
talks to. It exposes no HTTP API beyond a `/healthz` ping. All real traffic comes in over
RabbitMQ as JSON messages; results are returned via AMQP RPC.

Two retrieval pipelines run side-by-side in the same Python process:

1. **Text leg** — [LightRAG](https://github.com/HKUDS/LightRAG) (`lightrag-hku` from PyPI)
   built on top of OpenRouter's `text-embedding-3-small` and stored in Qdrant. Used for
   text descriptions, captions, analytics summaries. LightRAG also runs a knowledge-graph
   over chunks for hybrid retrieval.
2. **Visual leg** — a multimodal embedder (`google/gemini-embedding-2-preview` on
   OpenRouter by default) writing 3072-dim vectors to a separate Qdrant collection
   `meai_rag_visual_v2`. Used for cross-modal retrieval (text query → image hits).

Both legs are queried in parallel and the Ai service does the fusion + answer
synthesis. This service is intentionally retrieval-only — it does not build the
final answer.

## Communication

| Queue | Direction | Pattern | Body |
|---|---|---|---|
| `meai.rag.ingest` | Ai → RAG | one-way, durable, fire-and-forget | JSON ingest payload |
| `meai.rag.query`  | Ai → RAG | RPC (caller sets `replyTo` + `correlationId`) | JSON query payload; reply is JSON to `replyTo` |

There is no other transport. cf. `service/rabbit_consumer.py`.

### Ingest payloads (`meai.rag.ingest`)

```jsonc
// kind=text — caption + analytics
{ "kind": "text", "documentId": "facebook:<scope>:<postId>",
  "fingerprint": "<sha256-hex>", "content": "..." }

// kind=image — vision LLM describes, text-embeds the description
{ "kind": "image", "documentId": "facebook:<scope>:<postId>:img:0",
  "fingerprint": "<sha256-hex>", "imageUrl": "https://...",
  "caption": "...", "describePrompt": "..." }

// kind=image_native — multimodal embedder, image+caption vectors in same space
{ "kind": "image_native", "documentId": "facebook:<scope>:<postId>:vis2:0",
  "fingerprint": "<sha256-hex>", "imageUrl": "https://...",
  "caption": "...", "scope": "facebook:<scope>:", "postId": "..." }

// kind=texts — bulk text without per-id fingerprints
{ "kind": "texts", "content": ["...", "..."] }
```

`fingerprint` is opaque to this service. When a fingerprint matches what's already
stored in `<working_dir>/ingested_ids.json` for the same `documentId`, the message is
acked as `"skipped"`. Different fingerprint → existing doc deleted, new content
inserted (`"updated"`). Missing → fresh insert (`"ingested"`). See
`service/rag_engine.py::_reconcile`.

### Query payloads (`meai.rag.query`, RPC)

```jsonc
// op default: "query" — synthesized text answer via LightRAG
{ "query": "...", "documentIdPrefix": "facebook:<scope>:",
  "topK": 10, "mode": "hybrid", "onlyNeedContext": false }

// op="multimodal_query" — both text and visual hits, no synthesized answer
{ "op": "multimodal_query", "query": "...",
  "documentIdPrefix": "facebook:<scope>:",
  "topK": 8, "modes": ["text","visual"] }

// op="list_fingerprints" — what's currently indexed under a prefix
{ "op": "list_fingerprints", "documentIdPrefix": "facebook:<scope>:" }
```

Replies follow OpenAI-ish shape:

```jsonc
// "query" reply
{ "query": "...", "mode": "...", "topK": 8, "answer": "...",
  "matchedDocumentIds": ["..."] }

// "multimodal_query" reply
{ "query": "...", "topK": 8, "documentIdPrefix": "...",
  "text":   { "context": "...", "matchedDocumentIds": [...] },
  "visual": [ { "documentId":"...", "kind":"image|caption", "scope":"...",
                "imageUrl":"...", "caption":"...", "postId":"...",
                "score": 0.71 }, ... ],
  "visualError": null }

// "list_fingerprints" reply
{ "fingerprints": { "<docId>": "<sha>", ... }, "count": 3 }
```

## Repo layout

```
Rag.Microservice/
  Dockerfile           # python:3.10-slim. Pins lightrag-hku, openai, qdrant-client,
                       # aio-pika, fastapi, uvicorn, aiohttp at build time. Sets
                       # PIP_NO_INDEX=true at runtime so pipmaster (used by lightrag)
                       # fast-fails instead of hanging the worker.
  service/
    __init__.py
    main.py            # entrypoint — boots engine, rabbit consumer, /healthz server
    config.py          # env-driven Config dataclass + load_config()
    health.py          # tiny FastAPI app exposing /healthz only
    rabbit_consumer.py # aio_pika consumer for ingest + query queues
    rag_engine.py      # LightRAG wrapper + image_native + multimodal_query +
                       # ingested_ids registry + skip/update reconciliation
    multimodal_embedder.py
                       # OpenRouter /embeddings client for nested-content multimodal
                       # input. Has retry+backoff and image-download fallback;
                       # fast-fails on permanent provider rejections.
    qdrant_visual_store.py
                       # Direct Qdrant client for the visual collection
                       # (LightRAG only manages text collections)
    requirements.txt   # service-only deps; engine deps come from the Dockerfile
  .gitignore
  AGENTS.md            # this file
```

Anything outside `service/` and the Dockerfile is not used at runtime.

## Naming conventions

- **Document IDs** are opaque strings owned by the caller (Ai service). The Ai side
  uses `{platform}:{socialMediaId.N}:{postId}` for text docs and appends suffixes
  `:img:0` (image-describe) or `:vis2:0` (multimodal embed). This service only
  cares that they're unique, prefix-comparable strings.
- **Qdrant collections**:
  - `meai_rag` workspace + LightRAG-managed sub-collections (`entities`,
    `relationships`, `chunks`) for the text leg.
  - `meai_rag_visual_v2` for the multimodal vectors. The `_v2` suffix marks the
    Gemini Embedding 2 Preview era (3072-dim). Switching to a different multimodal
    model with a different dimension means **new collection name** — never reuse.
- **RabbitMQ queues** are kebab-dotted: `meai.rag.ingest`, `meai.rag.query`.
- **Status strings** in ingest replies: `ingested`, `updated`, `skipped`.
- **Logger names**: `rag-service.<area>` — e.g. `rag-service.engine`,
  `rag-service.rabbit`, `rag-service.qdrant-visual`,
  `rag-service.multimodal-embedder`.
- **Python style**: PEP 8, snake_case for functions/files, `from __future__ import
  annotations` on every module that uses PEP 604 unions, dataclass for `Config`.

## Embedding & LLM models

Configured via env vars; defaults below match what production compose ships.

| Role | Default model | Where it's used |
|---|---|---|
| Text embeddings (LightRAG) | `openai/text-embedding-3-small` (1536-dim) | text/image-describe documents |
| LLM completions (LightRAG, vision) | `openai/gpt-4o-mini` | answer synthesis + describe-then-embed for `kind=image` |
| Multimodal embeddings | `google/gemini-embedding-2-preview` (3072-dim) | `kind=image_native` + `op=multimodal_query` (visual leg) |

All routed via OpenRouter's OpenAI-compatible API.

## Persistence

- `WORKING_DIR` (default `/data/rag_storage`, mounted volume `rag-data`) holds:
  - LightRAG's `kv_store_*.json`, `graph_chunk_entity_relation.graphml`, etc.
  - Our own `ingested_ids.json` — `{documentId: fingerprint}` map for
    skip-or-update reconciliation.
- Vectors live in Qdrant (separate volume `qdrant-data`).

When dimension or namespace changes, the **service refuses to start** rather than
silently corrupt the collection. The visual store enforces this in
`QdrantVisualStore.initialize`.

## Skip / update / ingest behavior

When a `kind=text|image|image_native` ingest message has a `fingerprint` field:

```
            fingerprint provided?
                   │
        ┌──────────┴──────────┐
       no                    yes
        │                     │
   plain insert     existing fingerprint?
                   ┌────────────┴────────────┐
                  no                        yes
                   │                         │
              fresh insert        same as stored?
              ("ingested")    ┌──────────┴──────────┐
                             yes                   no
                              │                     │
                         skip + ack        delete existing,
                         ("skipped")       insert new
                                          ("updated")
```

Visual-collection ingests (`image_native`) are best-effort: if image-bytes embed
fails but caption-text embed succeeds (or vice versa), the message is recorded as
`"ingested"` with whichever vectors made it. If both fail, the message ack-fails
and is **not** written to `ingested_ids.json`, so the next ingest run retries it.

## Multimodal embedder reliability

OpenRouter free / preview tiers occasionally return transient `5xx` /
"No successful provider responses". The embedder retries 4× with
`[1s, 2.5s, 6s]` backoff. Permanent rejections — `URL_ROBOTED-ROBOTED_DENIED`
(Vertex respects robots.txt; FB CDN blocks Vertex), `Provided image is not valid`,
`INVALID_ARGUMENT` — are detected and short-circuit retries to avoid burning
quota on a doomed request. Reference: `multimodal_embedder.py::_embed`.

For image inputs the client first tries the URL directly and, on
non-permanent errors, falls back to fetching the bytes itself and resending as a
`data:` URL. Some providers (notably Gemini Embedding 2) reject data URLs too —
that's flagged as permanent so we don't double-spend.

## Env vars

Required:

| Name | Notes |
|---|---|
| `LLM_BASE_URL`, `LLM_API_KEY` | OpenRouter endpoint + key |

Optional with defaults:

| Name | Default | Purpose |
|---|---|---|
| `LLM_MODEL` | `openai/gpt-4o-mini` | LLM used by LightRAG for answers + image describe |
| `EMBED_BASE_URL` / `EMBED_API_KEY` | falls back to `LLM_*` | text-embedding endpoint |
| `EMBED_MODEL` / `EMBED_DIM` / `EMBED_MAX_TOKENS` | `text-embedding-3-small` / `1536` / `8192` | text embedder |
| `MULTIMODAL_EMBED_BASE_URL` / `_API_KEY` | falls back to `LLM_*` | multimodal embedder endpoint |
| `MULTIMODAL_EMBED_MODEL` / `_DIM` | `google/gemini-embedding-2-preview` / `2048` | multimodal embedder *(set DIM to match the model — this service refuses to start if it diverges from an existing collection)* |
| `MULTIMODAL_EMBED_MAX_CONCURRENCY` | `2` | bound on concurrent OpenRouter calls |
| `MULTIMODAL_VISUAL_COLLECTION` | `meai_rag_visual_v2` | Qdrant collection name for visual vectors |
| `RABBITMQ_URL` | derived from `RABBITMQ_{HOST,PORT,USER,PASS}` | full AMQP URL takes precedence |
| `RABBIT_INGEST_QUEUE` / `_QUERY_QUEUE` | `meai.rag.ingest` / `meai.rag.query` | queue names |
| `RABBIT_PREFETCH` | `4` | per-queue prefetch |
| `QDRANT_URL` / `QDRANT_API_KEY` / `QDRANT_NAMESPACE` | `http://qdrant:6333` / `(none)` / `meai_rag` | LightRAG vector store; namespace is also LightRAG's `workspace` |
| `WORKING_DIR` | `/data/rag_storage` | LightRAG state + ingested_ids registry |
| `LOG_LEVEL` | `INFO` | logger level |
| `HEALTH_PORT` | `8000` | only `/healthz` |

## Local dev

The service is designed to run inside the production compose stack, not standalone:

```
docker compose -f Backend/Compose/docker-compose-production.yml up -d --build rag-microservice
```

Manual tail:

```
docker logs -f rag-microservice
docker exec rabbit-mq rabbitmqctl list_queues name messages
```

Changes to anything under `service/` require a rebuild of the container —
`COPY service /app/service` happens at image-build time, not run time. Same for
`service/requirements.txt` (Dockerfile installs it before copying source).

## Adding a new ingest / query op

1. Add the handler method on `RagEngine` in `rag_engine.py`.
2. Wire dispatch in `RabbitConsumer._handle_query` (for query queue) — the
   dispatch is by `payload.get("op")` with `"query"` as the default.
3. Document the new payload shape in this file and in the Ai service's
   abstraction (`Application/Abstractions/Rag/IRagClient.cs` over there).
4. Mirror in the .NET `RabbitMqRagClient`.

## What to avoid

- Re-introducing the upstream RAG-Anything `raganything/` package source in this
  folder. We use `lightrag-hku` directly via PyPI; the upstream package is
  kept out by design (kept the image lean and avoids version drift). If you
  ever need its multimodal pipeline, prefer pinning a specific tagged release
  via `pip install raganything==X.Y.Z` in the Dockerfile.
- Returning HTTP from this service for anything other than `/healthz`. The
  contract is RabbitMQ-only.
- Mixing two embedding models into one Qdrant collection. Different dimensions
  = new collection name, always.
- Letting `ingested_ids.json` and Qdrant drift apart. If you delete a Qdrant
  collection you must also clear or migrate the registry entries that point
  into it, otherwise the next ingest looks like a "skip" but the data is gone.
- Synchronous filesystem writes inside `_handle_*`. The registry `_persist_ids`
  is async-locked for a reason — concurrent ingests would otherwise race on
  `ingested_ids.json`.

## Telemetry / debugging

- Successful ingests log `ingest done: {...}` at INFO.
- Failed ingests log `ingest failed for payload: {...}` at ERROR plus a
  traceback. Messages are acked even on failure (`requeue=False`) so a poison
  pill cannot loop the queue; re-run on the Ai side will requeue the
  fingerprint mismatch.
- Multimodal retries log a WARN per attempt with the elided body.
- LightRAG's own logger is `lightrag` and is configured via `setup_logger`.

## Related code in MeAI-BE

- Ai-side abstraction: `Backend/Microservices/Ai.Microservice/src/Application/Abstractions/Rag/`
  (`IRagClient`, `RagModels.cs`).
- Ai-side transport: `Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Rag/`
  (`RabbitMqRagClient.cs`, `RagOptions.cs`).
- Ai-side consumers / commands using the RAG: `Application/Recommendations/`.
- Compose env wiring: see `rag-microservice` stanza in
  `Backend/Compose/docker-compose-production.yml`.
