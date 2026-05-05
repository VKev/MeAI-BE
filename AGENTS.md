# AGENTS.md — MeAI-BE

This is the **master project document**. It used to be gitignored alongside per-service
`AGENTS.md` files; both have now been consolidated here. Anything specific to a
microservice that isn't trivially derivable from its source belongs in this file.

---

## 1. Project overview

- **.NET 10 microservices** for User, Ai, Feed, Notification, ApiGateway with Clean
  Architecture per service.
- **Python (3.10) sidecar microservice** for Rag (LightRAG + multimodal + VideoRAG).
- Shared C# code lives in `SharedLibrary` (auth, configs, contracts, gRPC `.proto` files,
  response models, middleware).
- Infra: Docker Compose for local; Terraform for AWS (ECS/EC2 or EKS); optional CloudFront / Cloudflare.
- Async messaging via RabbitMQ + MassTransit (saga state machines on the Ai service).
- Sync cross-service calls via gRPC (User ↔ Ai ↔ Feed; Ai ↔ Rag for sync ingest).
- Shared OpenRouter account is the LLM/embed/image-gen origin — billing is shared, see
  the cost rules below.

## 2. Repository layout (top-level)

```
Backend/
  Compose/                          # docker compose stacks, postgres init, seed-data
    docker-compose.yml              # dev placeholder stack
    docker-compose-production.yml   # prod-like stack — gitignored, treat as sensitive
    postgres/init.sql               # creates aidb + userdb on first boot
    seed-data/feed/runtime/*.json   # generated seed state, runs of compose mutate this
  Kubernetes/                       # manifests + kind cluster config
  Microservices/
    Microservices.sln
    Ai.Microservice/                # .NET — chat, drafts, recommendations, AI gen
    Feed.Microservice/              # .NET — public feed, profiles, analytics
    Notification.Microservice/      # .NET — notification API + SignalR hub
    Rag.Microservice/               # Python — LightRAG + multimodal + VideoRAG
    User.Microservice/              # .NET — auth, profile, resources, billing, social OAuth
    ApiGateway/                     # .NET — YARP reverse proxy with dynamic routes
    SharedLibrary/                  # .NET — shared C# code + .proto definitions
Terraform/                          # infra root (modules, scripts, main_*.tf)
Terraform-vars/                     # sanitized tfvars templates (checked in)
terraform-var/                      # private tfvars (gitignored)
.github/workflows/
AGENTS.md                           # this file (project-wide doc)
```

## 3. Service layout

### 3.1 .NET services (User / Ai / Feed / Notification)

```
Backend/Microservices/<Service>.Microservice/
  dockerfile                                  # lowercase 'dockerfile' (compose/CI also accept Dockerfile/DockerFile)
  src/
    Domain/         # entities, repository interfaces — no other layer dependency
    Application/    # CQRS via MediatR; feature-first folders with Commands/Queries/Models/Validators
    Infrastructure/ # EF Core, repos, migrations, gRPC clients, MassTransit consumers, sagas
    WebApi/         # ASP.NET controllers, OpenAPI/Scalar, YARP gateway runtime
  test/
    ArchitectureTest.cs               # rules enforced (see § 4)
```

Conventions:
- **Domain**: `Entities/`, `Repositories/` (interfaces).
- **Application**: feature-first folders (`Users/`, `Workspaces/`, `Recommendations/`, …) with `Commands/`, `Queries/`, `Models/`, `Validators/`.
- **Infrastructure**: `Context/`, `Repositories/`, `Migrations/`, `Configs/`, `Common/`, `Logic/` (`Logic/Services/`, `Logic/Consumers/`, `Logic/Sagas/`, `Logic/Payments/`, `Logic/Security/`, `Logic/Storage/`, `Logic/Threads/`, `Logic/TikTok/`, `Logic/Seeding/`, `Logic/Rag/`, …).
- **WebApi**: `Controllers/`, `Setups/`, `Middleware/`, `Mapping/`, `Properties/`.

### 3.2 Rag.Microservice (Python clean architecture)

```
Backend/Microservices/Rag.Microservice/
  dockerfile
  requirements.txt
  src/
    composition/        # entrypoint.py, config.py, container.py — manual DI
    domain/             # documents.py, queries.py — types + IngestPayload/IngestResult
    application/
      ports.py          # typing.Protocol interfaces (FingerprintRegistry,
                        # LightRagFacade, MultimodalEmbedderPort, VisualStorePort,
                        # ImageMirrorPort, VisionDescriberPort, VideoRagEnginePort,
                        # KnowledgeLoaderPort)
      knowledge_parser.py
      services/
        ingest_service.py
        query_service.py
        knowledge_bootstrap_service.py
        lazy_knowledge_bootstrap.py     # ensure_ready() — see § 9
    infrastructure/
      lightrag_facade.py          # wraps lightrag-hku; Jina rerank wired in;
                                  # monkeypatched QdrantVectorDBStorage.query for
                                  # per-call doc-id scoping (see § 9)
      multimodal_embedder.py      # OpenRouter /embeddings client (Gemini Embed 2)
      qdrant_visual_store.py      # `meai_rag_visual_v2` collection (3072-dim)
      s3_image_mirror.py
      vision_describer.py         # OpenAI-compatible vision LLM for `kind=image`
      knowledge_loader.py         # filesystem loader for src/knowledge/*.md
      fingerprint_registry.py     # JSON file at WORKING_DIR/ingested_ids.json
      video_rag/
        adapter.py
        downloader.py             # yt-dlp + requests
        _lib/                     # vendored upstream videorag library (renamed
                                  # from repo-root `videorag/` to live inside src/)
    transport/
      health_app.py               # FastAPI /health
      rabbit_consumer.py          # aio-pika; ingest queue + RPC query queue
      grpc/
        rag_ingest_servicer.py
        grpc_gen/                 # generated at image-build from rag_service.proto
    knowledge/                    # markdown knowledge files bootstrapped on
                                  # first request — see § 9
      content_formulas.md
      viral_hooks.md
      engagement_triggers.md
      visual_design.md
      platform_algorithm_signals.md
      image_design_creative.md
      image_design_branded.md
      image_design_marketing.md
```

**Conventions for Python**: PEP 8, snake_case, `from __future__ import annotations` on every
module that uses PEP 604 unions, dataclasses for config / payload types, manual DI in
`container.py` (no DI framework — clean-arch with ~12 services reads top-to-bottom like
a recipe). Loggers `rag-service.<area>`.

## 4. Architecture rules enforced by tests

- Domain must not depend on Application / Infrastructure / WebApi.
- Application must not depend on Infrastructure / WebApi.
- Infrastructure must not depend on WebApi.
- Handlers depend on Domain.
- Tests live at `Backend/Microservices/*/test/ArchitectureTest.cs`.
- There is an intended controller-to-MediatR dependency assertion in the architecture
  tests; verify the current test assembly target before relying on it.

For Rag.Microservice the equivalent is **Python Protocol-based ports**: `application/ports.py`
defines the interfaces, `infrastructure/` provides concrete adapters, `composition/` wires
them. Don't import `infrastructure.*` from `application.*`.

## 5. Naming conventions

- Folder/file names: PascalCase for C# (`AuthController.cs`, `CreateUserCommand.cs`); snake_case for Python.
- Projects/services: service folders end with `.Microservice`; gateway is `ApiGateway`; shared code is `SharedLibrary`.
- C# namespaces: file-scoped, mirror folders (`Domain.*`, `Application.<Feature>.<Commands|Queries|Models|Validators>`, `Infrastructure.*`, `WebApi.*`).
- CQRS types: `*Command` / `*Query`, handlers `*Handler`, validators `*Validator`.
- DTOs/models: `*Request`, `*Response`, or `*Model`.
- Setup extensions: `*Setup` classes in `WebApi/Setups`.
- Dockerfiles: `dockerfile` lowercase preferred (CI also accepts `Dockerfile`/`DockerFile`).
- RabbitMQ queues: dotted lowercase — `meai.rag.ingest`, `meai.rag.query`.
- Qdrant collections:
  - LightRAG-managed: `lightrag_vdb_chunks`, `lightrag_vdb_entities`, `lightrag_vdb_relationships` (all in workspace `meai_rag`).
  - Multimodal visual: `meai_rag_visual_v2` (3072-dim Gemini Embed 2). The `_v2` suffix
    marks the dim era — switching the multimodal model with a different dim means
    **new collection name**, never reuse.
  - VideoRAG frame-level: `meai_rag_video_segments` (3072-dim, frame-level granularity).
- Document IDs (RAG): caller-owned opaque strings, prefix-scoped:
  - Per-account posts: `{platform}:{socialMediaId.N}:{postId}` plus suffixes `:img:0` (image-describe),
    `:vis2:0` (multimodal embed), `:vid:0` (video).
  - Knowledge: `knowledge:<namespace>:<slug>` (e.g. `knowledge:content-formulas:fab-formula`).

## 6. Coding/build conventions

- Target framework: net10.0; `Nullable` and `ImplicitUsings` enabled.
- Build outputs are redirected to `Build/bin` and `Build/obj` per project.
- EF Core tooling expects custom `MSBuildProjectExtensionsPath`; targets are copied from
  `SharedLibrary/Configs/Infrastructure.csproj.EntityFrameworkCore.targets`.
- Generated folders under `Build/` are not source of truth; do not hand-edit.
- Facebook/Instagram/TikTok/Threads OAuth + Graph API logic must live in
  `Infrastructure/Logic/*` services with Application abstractions; commands only
  orchestrate via those interfaces.

## 7. Frontend/API response contract

- **Preserve the existing FE-facing response shape and status code** for any endpoint you
  touch. Do not rename, remove, reorder, or wrap top-level JSON fields on existing routes
  unless the frontend contract is being updated in the same change.
- Success responses typically return `Ok(result)` where `result` is
  `SharedLibrary.Common.ResponseModel.Result` / `Result<T>`.
- Business and validation failures flow through `HandleFailure(result)` so the response
  stays as `ProblemDetails` with the current `status`, `type`, `detail`, and optional
  `errors` extension shape.
- Unauthorized responses keep the current single-message contract per endpoint
  (`MessageResponse` or anonymous `{ message: "Unauthorized" }`). Do not introduce
  ad-hoc wrappers like `{ success, data }` or `{ error: ... }`.

## 8. Service map

| Service | Tech | Owns |
|---|---|---|
| **User** | .NET | auth, profile, resources / S3 presigned URLs, subscriptions / billing, social media OAuth + account metadata, workspaces, admin (users / subscriptions / transactions / config); gRPC services consumed by Ai and Feed |
| **Ai** | .NET | chats / chat sessions, AI generation (text, image, video), Kie/Veo callbacks, post builders / posts, draft-post generation saga, recommendations, async publish/unpublish/update consumers; gRPC clients to User and Feed; gRPC + AMQP RPC client to Rag |
| **Feed** | .NET | public feed, profiles, posts, comments, follows, reports, analytics snapshots, demo seed data; gRPC analytics consumed by Ai |
| **Notification** | .NET | notification REST API + SignalR hub `/hubs/notifications`. Gateway also exposes `/api/Notification/hubs/notifications` (prefix stripped) |
| **Rag** | Python | LightRAG text/entity/relation retrieval (Qdrant), multimodal cross-modal retrieval (Gemini Embed 2 in `meai_rag_visual_v2`), frame-level VideoRAG (`meai_rag_video_segments`), knowledge bootstrap from `src/knowledge/*.md`. **No HTTP API beyond `/health`** — all real traffic over RabbitMQ + gRPC |
| **ApiGateway** | YARP | dynamic routing for `/api/User`, `/api/Ai`, `/api/AiGeneration`, `/api/Notification`, `/api/Feed`, plus extras via `Services__{Service}__Host/Port` env. Aggregated Scalar UI at `/scalar`. Per-service Scalar at `/docs` |

The Rag service has **two retrieval pipelines running side-by-side**:
1. **Text leg** — LightRAG (`text-embedding-3-small`, 1536-dim) for text/captions/profile/analytics
2. **Visual leg** — multimodal embedder (`google/gemini-embedding-2-preview`, 3072-dim) for cross-modal retrieval

Both are queried in parallel; the Ai service does fusion + answer synthesis.

## 9. Rag.Microservice key invariants

These are non-obvious behaviors callers depend on.

### 9.1 Lazy knowledge bootstrap (run-once, blocking)

`LazyKnowledgeBootstrap.ensure_ready()` is awaitable + idempotent. The first incoming
RPC/gRPC handler call schedules the bootstrap and **awaits** it; every subsequent caller
awaits the same task and returns the moment it completes. After completion, calls return
instantly for the rest of the container's lifetime.

- Service start-up does **not** trigger bootstrap. The first request does.
- Knowledge files (`src/knowledge/*.md`) are bootstrapped via LightRAG entity-extraction;
  this takes ~90–180s cold (~25–80 docs depending on file count).
- The fingerprint registry persists in `rag-data` volume (`ingested_ids.json`), so
  container restart with a preserved volume only re-ingests changed knowledge files.

### 9.2 Ai → Rag wait-for-ready contract

The Ai-side `RabbitMqRagClient.WaitForRagReadyAsync(ct)` calls a dedicated
`op="wait_ready"` over the RPC queue. It uses a longer per-call timeout
(`Rag__WaitReadyTimeoutSeconds`, default 1800s = 30 min) so the FIRST cold draft-post
request can sit through the bootstrap. All other RPC calls keep the regular short
timeout (`Rag__RpcTimeoutSeconds`, default 60s).

Convention: any consumer that orchestrates downstream LLM/image-gen work **must**
`await _ragClient.WaitForRagReadyAsync(ct)` as Step 0, before any indexing or query
call. See `Infrastructure/Logic/Consumers/DraftPostGenerationConsumer.cs`.

### 9.3 Per-account Qdrant scoping

LightRAG's `QueryParam` no longer supports an `ids` filter. Per-account scoping is
implemented via a **monkeypatch** on `QdrantVectorDBStorage.query` (in
`infrastructure/lightrag_facade.py`) that consults a `ContextVar`-set allowlist of
`full_doc_id` values and adds a Qdrant `MatchAny` filter on top of LightRAG's built-in
workspace filter. The facade's `query()` sets the contextvar around `aquery()`.

- The fingerprint registry (`JsonFileFingerprintRegistry.matching_ids(prefix)`) supplies
  the allowlist by prefix-matching against `documentIdPrefix`.
- Only the **chunks** namespace is filtered. Entities/relations are derivative — if a
  chunk is out of scope, its entities aren't surfaced anyway.
- Multi-tenant isolation is correct under this scheme **only as long as the registry +
  Qdrant agree**. If you ever drop a Qdrant collection without clearing the registry,
  ingests will look like `skipped` but the data is gone. Always migrate or clear together.

### 9.4 Jina rerank wired into LightRAG

`rerank_model_func` is set to wrap `lightrag.rerank.jina_rerank` with the configured
`RERANK_API_KEY` / `RERANK_BASE_URL` / `RERANK_MODEL`. Default model is
`jina-reranker-v2-base-multilingual`. With this wired, LightRAG retrieval no longer
emits the `Rerank is enabled but no rerank model is configured` warning.

The Ai service ALSO does its own external Jina-m0 multimodal rerank on the candidate
pool (past-post images + fresh-topic images) in `DraftPostGenerationConsumer` — that's
a separate pass on different data. Don't conflate them.

### 9.5 RabbitMQ contracts

| Queue | Direction | Pattern | Body |
|---|---|---|---|
| `meai.rag.ingest` | Ai → Rag | one-way, durable, fire-and-forget | JSON ingest payload (kind = `text`/`image`/`image_native`/`video`/`texts`) |
| `meai.rag.query` | Ai → Rag | RPC (caller sets `replyTo` + `correlationId`) | JSON query payload (op = `query`/`multimodal_query`/`list_fingerprints`/`wait_ready`) |

For sync batch ingest the Ai service uses **gRPC** instead — `RagIngestService.IngestBatch`
on port 5006. Used by `IndexSocialAccountPostsCommand` so the call blocks until every
doc has been embedded + upserted + registered before the next pipeline step runs.

### 9.6 Ingest behavior

When a `kind=text|image|image_native` ingest message has a `fingerprint` field:
- No fingerprint stored for that `documentId` → fresh insert (`"ingested"`).
- Fingerprint matches → ack as `"skipped"` (idempotent re-run).
- Fingerprint differs → delete existing, insert new (`"updated"`).

The .NET wire convention surfaces `"skipped"` as `"unchanged"` (preserves prior contract).

### 9.7 Frame-level VideoRAG

VideoRAG ingests one Qdrant point **per frame** (~5 frames per ~5-second segment) into
`meai_rag_video_segments`. A 30s reel → ~6 segments → ~30 Qdrant rows. Query collapses
to "best frame per segment" before returning to the .NET caller, so the Ai service sees
one hit per relevant segment with the highest-scoring frame URL surfaced.

Frame URLs are S3-mirrored so OpenAI / OpenRouter can fetch them (FB CDN blocks Vertex
via robots.txt).

## 10. Cost rules — DO NOT IGNORE

- **Image-gen calls cost real money** (~$0.234 per draft-post fire on OpenRouter at the
  current pricing). Never trigger draft-post generation as a smoke test without explicit
  user approval naming the cost.
- The user's standing rule: **don't auto-test `/draft-posts`**. If you need to verify
  RAG / query / LLM paths, prefer cheap endpoints (`/api/Ai/recommendations/{id}/query`)
  or the `op="wait_ready"` round-trip — those use cheap LLM calls or none at all.
- OpenRouter credit failures surface as `HTTP 402 Payment Required` from
  `Image gen call failed: HTTP 402 body={...You requested up to N tokens, but can only
  afford M...}`. This is not a code bug — top up at <https://openrouter.ai/settings/credits>.
- The Brave Search call (fresh-topic image search) costs ~$0.003/call; safe for repeat
  testing.

## 11. Local dev quick refs

- Build/test: `dotnet build` / `dotnet test` from repo root.
- Run a single service: `dotnet run --project Backend/Microservices/User.Microservice/src/WebApi/WebApi.csproj`.
- Compose (dev): `docker compose -f Backend/Compose/docker-compose.yml up -d --build`.
- Compose (prod-like, what most testing uses): `docker compose -f Backend/Compose/docker-compose-production.yml up -d --build`.
- Rebuild a single service: `docker compose -f Backend/Compose/docker-compose-production.yml build <service> && docker compose ... up -d <service>`.
- Restart for a code change in Rag.Microservice: rebuild required (`COPY src /app/src`
  happens at image-build time).
- For env-only changes in ai-microservice: just `up -d ai-microservice` (env vars are
  re-read on container start).

### Default ports (prod compose)

| Service | Container port | Host port | Notes |
|---|---|---|---|
| API Gateway | 8080 | **2406** | All traffic enters here |
| User HTTP / gRPC | 5002 / 5004 | — | gRPC consumed by Ai + Feed |
| Ai HTTP / gRPC | 5001 / 5005 | — | gRPC consumed by Feed |
| Feed HTTP / gRPC | 5007 / 5008 | — | gRPC analytics consumed by Ai |
| Notification HTTP | 5006 | — | also serves SignalR `/hubs/notifications` |
| Rag gRPC / health | **5006** / 8000 | 5006 / — | gRPC published; HTTP only `/health` |
| Postgres | 5432 | 5432 | DBs `aidb`, `userdb` (init.sql) |
| Redis | 6379 | 6379 | sessions / refresh tokens |
| RabbitMQ | 5672 / 15672 | 5672 / 15672 | management UI on 15672 |
| Qdrant | 6333 | 6333 | LightRAG + multimodal + VideoRAG vectors |
| n8n | 5678 | 127.0.0.1:5678 | only via dev compose nginx |

(Service gRPC ports are internal unless explicitly published.)

## 12. API testing & temp files

- Use `curl` for manual API endpoint testing (`curl.exe` in PowerShell when needed).
- Test through the API Gateway (`localhost:2406`) unless you intentionally need direct service access.
- For auth flows, use cookie jar flags (`-c` / `-b`) or bearer tokens explicitly. Seed user
  is `user@gmail.com` / `12345678` (defined in `docker-compose-production.yml`); the Login
  endpoint is `POST /api/User/auth/login` with body
  `{ "emailOrUsername": "...", "password": "..." }`.
- Temp files for requests: create only under `<repo-root>/.temp/` (gitignored).
- In PowerShell: prefer JSON body files over inline `--data-raw` (quote handling can corrupt
  JSON). Write to `.temp/<case>.json` and use
  `curl.exe --data-binary "@.temp/<case>.json" -H "Content-Type: application/json"`.
- When reporting curl test results: include method, URL, request body, HTTP status, response
  body. Mask passwords, bearer tokens, refresh tokens, API keys, and other secrets.
- For third-party callbacks/webhooks, use `ngrok` to expose the gateway. If `ngrok` is
  already running, reuse the existing tunnel and fetch its URL before starting a new one.
- When the `ngrok` URL changes, update callback/redirect env values in
  `Backend/Compose/docker-compose-production.yml`, then restart prod compose. Restart flow:
  `down` then `up -d --build`.
- After test runs, remove temporary test artifacts from `.temp/` and don't commit sensitive data.

## 13. API Gateway (YARP)

- Runtime config generated in `Backend/Microservices/ApiGateway/src/Setups/YarpRuntimeSetup.cs`.
- Default routes for `/api/User` and `/api/Ai` are always added.
- When Ai is enabled, the gateway also adds `/api/Gemini` as an alias route.
- Extra services via `Services__{Service}__Host` + `Services__{Service}__Port`
  (or `{PREFIX}_MICROSERVICE_HOST/PORT`).
- OpenAPI aggregation pulls `/openapi/v1.json` from each .NET service.
- Runtime config is written to `yarp.runtime.json` in the gateway content root.
- Gateway docs UI: `ENABLE_DOCS_UI` toggles the aggregated Scalar UI at `/scalar`.
- Service-local Scalar UIs at `/docs`.
- Keep compose/env naming aligned with `Backend/Microservices/ApiGateway/src/Program.cs`.

## 14. Scalar / OpenAPI docs

- Scalar UI is generated from each .NET service's ASP.NET OpenAPI document
  (`app.MapOpenApi()` + `app.MapScalarApiReference("docs", ...)`) and aggregated by the
  gateway.
- When adding/changing endpoints: add `[ProducesResponseType]` for success, validation/
  business failures, and unauthorized responses; use request/response DTO records that
  reflect the actual JSON contract; align route names, auth attributes, HTTP verbs.
- Custom request-body / schema behavior: add an OpenAPI transformer under
  `WebApi/Setups/OpenApi/` and register it in `Program.cs` (mirror the Resources multipart
  transformer).

## 15. Docker & Compose

- `Backend/Compose/docker-compose.yml`: dev placeholder stack (n8n + nginx).
- `Backend/Compose/docker-compose-production.yml`: prod-like stack — **gitignored, treat
  as sensitive**. Contains real OpenRouter / Jina / Brave / Stripe / Facebook keys etc.
  If you inspect it, never echo literal secrets back into chat, logs, commits, or new
  tracked files.
- Compose service names are kebab-case: `ai-microservice`, `user-microservice`,
  `rag-microservice`, `api-gateway`, etc.
- Postgres init: `Backend/Compose/postgres/init.sql` creates `aidb` + `userdb` on first
  boot.
- **Volumes** (top-level in compose):
  - `postgres-data` — never delete unless wiping users/posts state.
  - `qdrant-data` — vectors. Drop together with `rag-data`.
  - `rag-data` — LightRAG storage + `ingested_ids.json` registry. Drop together with `qdrant-data`.
  - `redis-data`, `rabbitmq-data`, `n8n-data` — typically safe to drop.
- Production compose enables `AutoApply__Migrations` and seed data. Running prod compose
  mutates `Backend/Compose/seed-data/feed/runtime/*.state.json` — treat those as generated
  local artifacts unless the task is specifically about seed state.
- Production user-service S3 currently points at the Terraform backend / app-download
  bucket. If changing storage behavior, keep Terraform S3 CORS, compose `S3__*`, and FE
  download behavior aligned.

## 16. Recent endpoint contracts (FE-facing)

### 16.1 `POST /api/AiGeneration/post-prepare` (or `/post/prepare`)

Persists a `PostBuilder` containing one empty draft `Post` per requested platform group.
**Per-platform `resourceIds` are NEVER auto-filled from the builder-level list** (this
behavior was removed). The builder owns the resource bundle; per-platform Posts only
carry resources the caller explicitly bound to that platform.

- `resourceIds` (request, builder-level): optional list. Must be a SUPERSET of any
  per-platform `resourceIds` that are provided.
- `socialMedia[i].resourceIds`: stays exactly as the caller sent it. Empty stays empty
  (Post.Content.ResourceList persists as `[]`).
- Validation: per-platform list must be ⊂ builder list when builder is non-empty;
  combined union of (builder + per-platform) must contain ≥ 1 resource (else 400
  `Resource.Missing`).

### 16.2 `GET /api/Ai/post-builders/{id}`

Returns `PostBuilderDetailsResponse` with builder-level resources hydrated to the SAME
shape as per-post `media[]`:

```jsonc
{
  "id": "...",
  "type": "posts",
  "resources": [                 // hydrated, single source of truth — no separate `resourceIds`
    {
      "resourceId": "<guid>",
      "presignedUrl": "https://s3.../...?X-Amz-...",
      "contentType": "image/jpeg",
      "resourceType": "image"
    }
  ],
  "socialMedia": [
    { "socialMediaId": null, "platform": "tiktok", "type": "posts", "posts": [ ... ] }
  ],
  ...
}
```

Hydration is best-effort — a `IUserResourceService` failure falls back to empty
`resources[]` rather than failing the whole detail call. Do not re-introduce the redundant
`resourceIds: Guid[]` field.

### 16.3 Improve-existing-post (RecommendPost) endpoints

Async pipeline that suggests a replacement caption and/or image for an EXISTING
post. Mirrors the draft-post flow but anchors RAG retrieval on the original post
rather than a fresh user prompt, and persists outputs on a separate
`RecommendPost` row WITHOUT modifying the original `Post`.

| Endpoint | Verb | Body | Returns |
|---|---|---|---|
| `/api/Ai/recommendations/posts/{postId}/improve` | POST | `StartImprovePostRequest` | `202 Accepted` + `RecommendPostTaskResponse` (status=`Submitted`, with `correlationId` and the new `RecommendPost.Id`) |
| `/api/Ai/recommendations/posts/{postId}/improve` | GET | – | `RecommendPostTaskResponse` for the most recent improvement of this post |

**Request body** (`StartImprovePostRequest`):
- `improveCaption: bool` — at least one of `improveCaption` / `improveImage` must be true (else 400 `ImprovePost.NothingToImprove`).
- `improveImage: bool`.
- `style?: "creative" | "branded" | "marketing"` — optional. When omitted, inherits the original post's stored style; final fallback is `branded`.
- `userInstruction?: string` — free-form steering ("make caption more playful", "cooler palette", etc.). Forwarded into both caption + image-brief prompts.

**Replace-on-rerun**: every new submit on the same post hard-deletes any existing
`RecommendPost` for that post id and inserts a fresh one. There is no history of
past suggestions; only the most recent run is reachable. The unique index on
`Post.RecommendPostId` (filtered on NOT NULL) is the safety net under concurrent
double-submit.

**Pipeline** (see `Infrastructure/Logic/Consumers/RecommendPostGenerationConsumer.cs`):
1. Step 0 — `WaitForRagReady` (same contract as draft-post).
2. Step 1 — re-index the social account (if `Post.SocialMediaId` is set; skipped otherwise — orphan drafts run unindexed).
3. Step 2 — RAG `QueryAccountRecommendationsQuery` anchored on the **original caption** (skipped when no SocialMediaId or empty caption — falls back to unscoped).
4. Step 3 — caption regen with the **"improve" system prompt** (skipped when `improveCaption=false`). Original caption + image refs + `userInstruction` + RAG context all go in.
5. Step 3.4 — style-knowledge fetch (`knowledge:image-design-{style}:`) — only when `improveImage=true`.
6. Step 4 — image-brief LLM (JSON output: `prompt`, `aspect_ratio`, `style_notes`) → image-gen with the original image as primary reference, then S3 upload (skipped when `improveImage=false`). **Costs ~$0.234 per fire** (image-gen call).
7. Step 5 — persist outputs on the `RecommendPost` row, mark `Completed`, publish `ai.draft_post_generation.completed` notification.

**The original `Post` is NEVER modified.** Suggested replacement caption/image
live on `RecommendPost.ResultCaption` / `RecommendPost.ResultResourceId` /
`ResultPresignedUrl`. A future "accept" endpoint to apply the suggestion to the
original post is out of current scope.

### 16.4 `POST /api/Ai/recommendations/{socialMediaId}/draft-posts` + consumer

Async pipeline (see `DraftPostGenerationConsumer`):
1. Step 0 — `WaitForRagReady` (blocks until rag knowledge bootstrap done; first cold call
   waits ~90–180s, subsequent calls instant).
2. Step 1 — `IndexSocialAccountPostsCommand` (fetch FB Graph posts → fingerprint diff →
   sync gRPC `IngestBatch` for new/changed only).
3. Step 2 — `QueryAccountRecommendationsQuery` (multimodal RAG; text-leg via LightRAG
   hybrid mode + Jina rerank, visual-leg via Qdrant `meai_rag_visual_v2`, video-leg via
   `meai_rag_video_segments`).
4. Step 3 — caption gen (gpt-4o-mini multimodal, style-aware).
5. Step 3.3 — fresh-topic image search (Brave) — references for style/subject.
6. Step 3.35 — Jina-m0 multimodal rerank of (past-post + fresh-topic) candidate pool.
7. Step 3.4 — fetch style-knowledge for the requested style
   (`knowledge:image-design-{style}:*`).
8. Step 4 — image-gen (Kie / OpenRouter image model). **Costs ~$0.234 per fire.**
9. Step 5 — persist result, mark `Completed`.

HTTP returns 202 with `correlationId`; poll `draft_post_tasks` row by correlationId for
status.

## 17. Subscription / billing invariants

- Subscription plan delete is a **soft delete**: set `IsActive=false`, `IsDeleted=true`,
  `DeletedAt`, keep the row readable so user history can still display old plan name/price.
- Deleting an active plan marks current active user subscriptions for that plan as
  `non_renewable`; do NOT hard-delete or expire them. They remain current until
  `EndDate` and display `displayStatus: "No recurring"`.
- `CurrentUserSubscriptionResponse` and admin user-subscription responses include
  `displayStatus`, `isAutoRenewEnabled`, `autoRenewStatus` — FE should use these instead
  of deriving labels from raw `status`.
- User auto-renew toggle: `POST /api/User/subscriptions/current/auto-renew`
  body `{ "enabled": true|false }`. Disabling sets to `non_renewable`, keeps access until
  `EndDate`, cancels local scheduled changes, sets Stripe `cancel_at_period_end=true`
  when Stripe state exists. Enabling requires an active non-deleted plan + Stripe recurring
  subscription, sets Stripe `cancel_at_period_end=false`, returns status `Active`.
- Admin user-subscription APIs at `/api/User/admin/user-subscriptions/...` — edit user's
  subscription status only, not the plan catalog row.
- Admin status update: `POST .../{id}/status` body
  `{ "status": "...", "reason": "..." }`. `reason` is required, may contain HTML for
  email rendering, publishes `user.subscription.status_changed` notification + best-effort
  email. **In-app notification `message` must stay plain text**; preserve raw HTML in
  payload fields like `reasonHtml`.
- Admin auto-renew update: `POST .../{id}/auto-renew` body
  `{ "enabled": true|false, "reason": "..." }`. Same rules — reason required, HTML allowed,
  publishes `user.subscription.auto_renew_changed` + email.
- Stripe card APIs at `/api/User/billing/cards`: `GET` lists cards, `POST` creates a
  Stripe SetupIntent for adding a card, `POST .../{paymentMethodId}/default` switches the
  default card. **Never accept raw card numbers in the backend.**
- Users have nullable `stripe_customer_id`; card APIs create/backfill it as needed. New
  subscription purchases must reuse this customer ID instead of creating duplicate Stripe
  customers.

## 18. Kubernetes

- Manifests in `Backend/Kubernetes/manifests/*.yaml` (Deployments, Services, PVCs, Secrets).
- Local kind cluster config in `Backend/Kubernetes/clusters.yaml` (NodePort mappings).
- EKS manifests can be injected via `k8s_microservices_manifest` in tfvars.
- NodePort defaults wired into ALB when `use_eks = true`.

## 19. Terraform (AWS)

- Root: `Terraform/` (VPC, ALB, ECS, EKS, RDS, CloudFront/Cloudflare, ACM, SES).
- Platform toggle: `use_eks` in tfvars.
  - `use_eks = true` → applies `k8s_microservices_manifest` from `k8s.auto.tfvars`.
  - `use_eks = false` → ECS/EC2 uses service groups from `ecs_groups.auto.tfvars`.
- Service definitions live in `Terraform-vars/*-service.auto.tfvars` (sanitized templates).
- Private tfvars live in `terraform-var/` (gitignored); keep sanitized copies in
  `Terraform-vars/` in sync.
- Scripts: `merge_tfvars.py` (merge), `export_tf_env.py` (export env),
  `resolve_placeholders.py` (replace `TERRAFORM_*`), `wait_for_services.py` (ECS wait).

### tfvars parsing quirk

`Terraform/scripts/merge_tfvars.py` runs a `_strip_hcl_quotes()` pass over every HCL-parsed
dict before merging — current `python-hcl2` versions return string values with HCL quote
delimiters embedded (`region = "us-east-1"` → Python string `'"us-east-1"'`). Without this
pass, `export_tf_env.py` writes `AWS_REGION="us-east-1"` to `$GITHUB_ENV` and
`aws-actions/configure-aws-credentials@v4` rejects with `Region is not valid: "us-east-1"`.
If you rewrite the merge step, preserve that strip — HCL strings can never legitimately
start AND end with a literal `"`, so stripping one matched outer pair is safe. JSON tfvars
bypass the pass entirely (`json.load()` is clean).

### tfvars deployment

Private tfvars values are NOT deployed from `terraform-var/` (gitignored); they live as
`TERRAFORM_VARS_*` GitHub Environment secrets, one per tfvars file
(`TERRAFORM_VARS_K8S` → `k8s.auto.tfvars`, `TERRAFORM_VARS_COMMON` → `common.auto.tfvars`,
…). Every workflow's "Merge tfvars" step materializes each secret into a file via `jq` —
valid JSON content gets `.auto.tfvars.json`, otherwise `.auto.tfvars` (HCL). Local
`terraform-var/` is a working copy only; to deploy a change, paste the new content into
the matching GH secret.

## 20. CI/CD (GitHub Actions)

- `full-deploy.yml` — bootstrap backend, build/push images, Terraform plan/apply/destroy.
- `terraform-deploy.yml` — infra-only plan/apply/destroy.
- `build-and-push-ecr.yml` — build/push microservice images to ECR.
- `build-and-push-dockerhub.yml` — build/push to Docker Hub.
- `bootstrap-terraform-backend.yml` — create S3/DynamoDB backend.
- `acm-certificate.yml` — ACM cert via Cloudflare DNS.
- Destructive: `nuke-aws-except-ecr.yml`, `erase-ecr.yml` (explicit confirmations required).

## 21. Security & secrets

- Do not commit real secrets or keys.
- `terraform-var/` is private and gitignored; keep `Terraform-vars/` sanitized.
- `Backend/Compose/docker-compose-production.yml` contains sensitive values; treat as
  local-only and rotate if used. **Never echo literal secrets back into chat responses,
  logs, commits, screenshots, or new tracked files.**
- `.env` files are gitignored; use env vars or tfvars for configuration.

## 22. Adding or changing a service

- New .NET service = new folder with `src/Domain|Application|Infrastructure|WebApi` + `test`.
- Add DI wiring in `WebApi/Program.cs` and update setup extensions in `WebApi/Setups`.
- Add a `dockerfile` (lowercase preferred) so compose and CI can detect it.
- Update Terraform service definitions and ECS groups OR k8s manifest if the service
  is deployable.
- For Python service additions, mirror the Rag.Microservice clean-arch shape
  (`composition/`, `domain/`, `application/{ports,services}`, `infrastructure/`,
  `transport/`).

## 23. Platform analytics quirks

- `GetSocialMediaPlatformPostAnalyticsQuery` has a snapshot-cache path
  (`post_metric_snapshots`) that only persists numeric metrics + `PostPayloadJson` — does
  NOT persist `CommentSamples`. Facebook, TikTok, Threads bypass this cache because they
  need fresh reply detail; Instagram still uses it. If you add a new platform with comment
  samples, either bypass the cache OR extend `SocialPlatformPostMetricSnapshotMapper` to
  serialize comments too.
- Threads `/{id}/replies` is user-scoped (= `GET /me/replies`) and 500s when queried with
  a post id. Use `/{id}/conversation` for replies to a specific post, then filter the
  root by comparing `item.Id` to the queried post id. **Avoid requesting the `is_reply`
  / `root_post` fields** — Meta 500s on tokens without the tier/permissions for them.
- Threads reply endpoints require `threads_read_replies` (read) and/or
  `threads_manage_replies` (write) OAuth scopes. Users must re-run the OAuth flow for
  scope changes to stick — existing access tokens are immutable.

## 24. What to avoid

- Cross-layer dependencies that violate Clean Architecture rules.
- Storing secrets in tracked files or logs.
- Changing `Build/obj` or `Build/bin` conventions unless updating all projects + EF tooling.
- Returning HTTP from Rag.Microservice for anything other than `/health` — its contract
  is RabbitMQ/gRPC only.
- Mixing two embedding models into one Qdrant collection — different dimensions = new
  collection name, always.
- Letting `ingested_ids.json` and Qdrant drift apart — they MUST be cleared/migrated
  together.
- Synchronous filesystem writes inside Rag's Rabbit `_handle_*` methods — the registry
  `_persist_ids` is async-locked for a reason; concurrent ingests would otherwise race
  on `ingested_ids.json`.
- Re-introducing the upstream `raganything/` package source in `Rag.Microservice/`. Use
  `lightrag-hku` directly. The vendored `videorag` library lives at
  `infrastructure/video_rag/_lib/` and should stay there.
- Re-introducing the auto-fill of per-platform resourceIds from builder-level in
  `PrepareGeminiPostsCommand` — that was deliberately removed.
- Adding a redundant `resourceIds: Guid[]` field back to `PostBuilderDetailsResponse`
  next to `resources[]` — `resources[].resourceId` is the single source of truth.
- Mutating the original `Post` inside `RecommendPostGenerationConsumer` — the
  contract is to **suggest** replacements (caption + image) on the `RecommendPost`
  row, NOT apply them to the original post. `Post.RecommendPostId` is the only
  field on `Post` the improve flow may touch.
- Keeping multiple `RecommendPost` rows per `Post`. The system holds at most ONE
  active suggestion per post — replace-on-rerun is intentional and is enforced by
  both the start command (hard-delete prior row) AND the unique index on
  `Post.RecommendPostId` (filtered on NOT NULL).
