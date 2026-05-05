# MeAI RAG Architecture — Full Pipeline Reference

> A complete walkthrough of how a draft post is generated end-to-end, illustrated with two example past posts ingested for the *Meai – Digital Camera* page.

## Example posts used throughout

| | **Post A** — Image | **Post B** — Video Reel |
|---|---|---|
| `postId` | `1122787180911554_122099621295294827` | `1122787180911554_122099400000000000` |
| Caption | *"Meai - Digital Camera đã cập nhật ảnh đại diện của mình."* | *"Hands-on DJI Osmo Mobile 7 Pro — xem ActiveTrack thực tế!"* |
| Media | One JPG (FB CDN): stylized camera lens + MeAI logo | 30-second MP4 (FB Reel) — Vietnamese voiceover, 4 visible scenes (gimbal in hand, ActiveTrack demo, foldable mode, controls close-up) |
| `MediaType` | `image` | `video` |

Page (`socialMediaId = 019dedac-…`), platform `facebook`, RAG namespace prefix `facebook:019dedac6365…:`.

---

## 1. Indexing pipeline (writing into RAG)

Triggered automatically by `IndexSocialAccountPostsCommand` at the start of every `/draft-posts` call. SHA-256 fingerprints make repeat indexes idempotent.

### 1.1 Documents created per post

```
Page profile     → 1 text doc                  (always-on, single per page)
Post A (image)   → 3 docs                      (text / image-describe / image-native)
Post B (video)   → 4 docs                      (text / thumb-describe / thumb-native / video)
```

### 1.2 Document shapes

| docId | kind | content / fields | What rag-microservice does with it |
|---|---|---|---|
| `…:profile` | `text` | Name, Category, About, Website, Email, Phone, Location | LightRAG `ainsert` → entity-extract via gpt-4o-mini → embed (text-embedding-3-small, 1536-dim) → upsert into `lightrag_vdb_chunks/entities/relationships` |
| `…:1122…294827` (Post A text) | `text` | Caption, MediaType, PublishedAt, engagement counts, Permalink | Same LightRAG pipeline as profile |
| `…:1122…294827:img:0` (Post A) | `image` | imageUrl + describePrompt | Vision LLM (gpt-4o-mini) describes the image; description goes through LightRAG as text |
| `…:1122…294827:vis2:0` (Post A) | `image_native` | imageUrl + caption + scope + postId | (1) Mirror image to S3; (2) Gemini Embedding 2 Preview produces a 3072-dim multimodal vector of (image+caption); (3) upsert into `meai_rag_visual` with payload `{document_id, scope, post_id, image_url, mirror_s3_key, caption}` |
| `…:1122…000000000` (Post B text) | `text` | Caption + MediaType=video + engagement | Same LightRAG pipeline |
| `…:vid-thumbnail:img:0` (Post B) | `image` | Reel thumbnail URL + describePrompt | Vision LLM describes the thumbnail |
| `…:vid-thumbnail:vis2:0` (Post B) | `image_native` | Thumbnail URL + caption | S3-mirror + Gemini multimodal embedding of thumbnail |
| `…:1122…000000000:vid:0` (Post B) | `video` | videoUrl + platform + socialMediaId + postId + scope | (1) yt-dlp downloads (DASH muxing for FB reels); (2) ffmpeg splits into ~5-second segments → ~6 segments; (3) Whisper-1 transcribes audio per segment; (4) **Per segment, sample 5 frames + upload each to S3 + Gemini Embedding 2 encodes EACH frame individually into a 3072-dim vector** (one batched API call per segment); (5) upsert ONE Qdrant point per frame into `meai_rag_video_frames` with payload `{video_name, post_id, scope, index, segment_name, frame_index, frame_url}`. **A 6-segment video produces ~30 frame rows, not 6 segment rows** — the granularity at retrieval is per-frame, but query collapses to "best frame per segment" before returning to the .NET caller. |

### 1.3 Transport

```
ai-microservice → POST http://rag-microservice:5006/rag.RagIngestService/IngestBatch  (gRPC)
                  blocks until ALL docs are written
                  ↑ critical for downstream RAG queries to see fresh data ↑
```

### 1.4 Final Qdrant point counts (after both posts indexed)

| Collection | Post A contribution | Post B contribution |
|---|---:|---:|
| `lightrag_vdb_chunks` | ~3 | ~5 |
| `lightrag_vdb_entities` | ~5 | ~8 |
| `lightrag_vdb_relationships` | ~3 | ~6 |
| `meai_rag_visual` | 1 | 1 (thumbnail) |
| `meai_rag_video_frames` *(was `meai_rag_video_segments`)* | 0 | **~30 (5 frames × 6 segments)** |

> **Migration note** — the legacy collection `meai_rag_video_segments` (1 vector per segment) is no longer written to. Default collection name is now `meai_rag_video_frames`. Existing legacy rows are orphaned and can be safely dropped; no live code reads from them anymore.

---

## 2. Draft generation pipeline

Triggered by `POST /api/Ai/recommendations/{socialMediaId}/draft-posts`. Async via RabbitMQ → `DraftPostGenerationConsumer`.

### 2.0 Stage map (high level)

```
┌─────────────────────────────────────────────────────────────────────────┐
│  IndexSocialAccountPostsCommand           (sync gRPC ingest)            │
└──────────────────────────┬──────────────────────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  QueryAccountRecommendationsQuery                                       │
│   ├─ Leg 1: rag.MultimodalQuery (text + visual + video)                 │
│   ├─ Leg 2: knowledge:content-formulas:platform-mapping-facebook        │
│   ├─ Leg 3: knowledge:* semantic match                                  │
│   └─ Leg 4: facebook:<smid>:profile                                     │
│        ▼                                                                │
│   Reciprocal Rank Fusion → fused references list                        │
│        ▼                                                                │
│   ★ Video-segment rerank (Jina) — per post pick best segment            │
│        ▼                                                                │
│   ★ Text rerank (Jina) — reorder + threshold + cap                      │
│        ▼                                                                │
│   Recommendation LLM (gpt-4o-mini multimodal) — writes recommendation   │
└──────────────────────────┬──────────────────────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  Caption LLM (gpt-4o-mini, style-aware, plain-text only)                │
│        ▼                                                                │
│  Brave image search → fresh-topic image candidates                      │
│        ▼                                                                │
│  Image-ref pool = past-post candidates + fresh-topic candidates         │
│        ▼                                                                │
│  ★ Image rerank (Jina, multimodal) — pick best refs by visual content   │
│        ▼                                                                │
│  Style knowledge fetch (knowledge:image-design-{style}:*)               │
│        ▼                                                                │
│  Image-brief LLM (gpt-4o-mini) — JSON brief                             │
│        ▼                                                                │
│  Image generation (gpt-5.4-image-2)                                     │
│        ▼                                                                │
│  S3 upload + Posts row + Notification                                   │
└─────────────────────────────────────────────────────────────────────────┘
```

The three ★ steps are the **three rerank passes**, each scoped narrowly:

| Pass | Where | What it scores | Output |
|---|---|---|---|
| **Video-segment rerank** | inside `QueryAccountRecommendationsQuery` | Multiple matched transcripts for the SAME video post | Drops below 0.25 + caps at top 2 segments per post (concatenated). Clears segment fields entirely if every segment is below threshold |
| **Text rerank** | inside `QueryAccountRecommendationsQuery` | All fused references' captions + (best) transcript | Reorders + drops below 0.20 + caps at 10 |
| **Image rerank** | inside `DraftPostGenerationConsumer` | Image URLs (past-post static + **per-frame video URLs** + fresh-topic from Brave) | Pixel-aware visual selection — drops below 0.40, caps at request.maxReferenceImages (≤8). **A single video post can contribute 1 thumbnail + N segment-best-frame URLs to the pool**, so the rerank picks the visually best of all of them |

### 2.1 Walkthrough with the two example posts

A draft request: `{ "userPrompt": "create content about DJI Osmo Mobile 7 Pro", "style": "branded" }`.

#### Step 2.1.a — Multimodal RAG retrieval (Leg 1)

`rag-microservice` receives the multimodal query and runs three sub-queries against Qdrant in parallel:

| Mode | What returns for this draft |
|---|---|
| `text` | LightRAG context for "DJI Osmo Mobile 7 Pro" against the page's text docs. Both Post A (caption "ảnh đại diện") and Post B (caption "Hands-on DJI Osmo") match — Post B much higher because the caption directly mentions the topic. Profile + entity graph also surfaces. |
| `visual` | Cosine over Gemini multimodal embeddings in `meai_rag_visual`. Post A's logo image and Post B's gimbal-in-hand thumbnail both appear; thumbnail ranks higher because the visual concept is closer to "smartphone gimbal". |
| `video` | Cosine over `meai_rag_video_frames` at the **frame** level (Post B has ~30 rows: 6 segments × 5 frames each). The rag-microservice over-fetches `top_k × 5` frame hits, then collapses to one hit per segment by keeping the **highest-scoring frame** within each segment. The `frame_url` of that winning frame rides through to the .NET response, alongside `segment_index` + `caption` + `transcript`. Multiple segments per post can match (one row per segment after collapse). |

#### Step 2.1.b — RRF fusion

Fuses text + visual + video ranks into one list keyed by post id:

```
Pre-rerank fused refs (RRF order):
  [1] Post B — score 0.092 — Source=text+visual+video
       caption=         "Hands-on DJI Osmo Mobile 7 Pro — xem ActiveTrack thực tế!"
       imageUrl=        s3://…/post-B-thumb.jpg                    ← thumbnail (image_native)
       videoSegmentTime= "00:00:05.000"      ← FIRST matched segment (RRF default)
       videoTranscript=  "Đây là DJI Osmo Mobile 7 Pro mới nhất…"
       videoFrameUrls=  [s3://…/seg_01_frame_02.jpg,                ← best frame per matched segment
                         s3://…/seg_03_frame_03.jpg,                  (collected from frame-level
                         s3://…/seg_04_frame_01.jpg]                   Qdrant rows, one URL per
                                                                       segment that hit)
  [2] Post A — score 0.061 — Source=text+visual
       caption=  "Meai - Digital Camera đã cập nhật ảnh đại diện…"
       imageUrl= s3://…/post-A-logo.jpg
  [3] Profile doc — score 0.015 — Source=text
       (page profile entity, no image)
```

#### Step 2.1.c — Video-segment rerank ★ (NEW)

Post B has 6 matched segments. Their transcripts:

| Segment time | Transcript snippet | Pre-rerank order |
|---|---|---:|
| `00:00:00` | *"Chào mọi người, hôm nay mình sẽ trên tay…"* | 1 |
| `00:00:05` | *"Đây là DJI Osmo Mobile 7 Pro mới nhất…"* | 2 |
| `00:00:11` | *"…tính năng ActiveTrack, các bạn xem máy ảnh tự bám theo chủ thể…"* | 3 |
| `00:00:18` | *"Phần xếp gọn rất ấn tượng, nhỏ hơn đời trước…"* | 4 |
| `00:00:24` | *"Pin trâu hơn, lên đến 10 tiếng…"* | 5 |
| `00:00:29` | *"Cảm ơn các bạn đã xem!"* | 6 |

Reranked against query *"create content about DJI Osmo Mobile 7 Pro"*, then filtered with `threshold=0.25` + `cap=2`:

```
score=0.78  segment 00:00:11 — "...ActiveTrack, máy ảnh tự bám theo..."   ← KEPT (rank 1)
score=0.71  segment 00:00:05 — "Đây là DJI Osmo Mobile 7 Pro..."          ← KEPT (rank 2)
score=0.45  segment 00:00:18 — "Phần xếp gọn rất ấn tượng..."             ← above threshold but exceeds cap → DROPPED
score=0.38  segment 00:00:24 — "Pin trâu hơn..."                          ← above threshold but exceeds cap → DROPPED
score=0.18  segment 00:00:00 — generic intro                              ← below threshold → DROPPED
score=0.10  segment 00:00:29 — generic outro                              ← below threshold → DROPPED

Video segment rerank for postId=Post B: kept 2/6 segments (threshold=0.25, cap=2);
top score=0.780 time=00:00:11
```

Both top segments (**ActiveTrack** at `00:00:11` and the **product intro** at `00:00:05`) survive — they're concatenated into the reference's transcript field for the LLM:

```
videoSegmentTime  = "00:00:11, 00:00:05"
videoTranscript   = "[00:00:11] ...ActiveTrack, máy ảnh tự bám theo... | [00:00:05] Đây là DJI Osmo Mobile 7 Pro..."
videoFrameUrls    = [s3://…/seg_03_frame_03.jpg,    ← winning frame of 00:00:11 segment
                     s3://…/seg_01_frame_02.jpg]    ← winning frame of 00:00:05 segment
```

The `videoFrameUrls` list is also narrowed at this step — only the URLs of segments that survived the threshold + cap remain. Frames belonging to dropped segments (intro/outro/etc.) are removed before the list flows into the image-rerank pool.

This replaces the RRF-default "first match wins" behavior. The recommendation LLM now sees the **two most relevant moments** of the video, in score order, with timestamps prefixed so it can cite them naturally ("according to the moment at 00:00:11 of your earlier video…"), and the **image-gen pipeline gets the actual frames** of those moments — not just the static thumbnail.

**Failure case**: if every matched segment scores below threshold (no relevant moment found), the segment fields AND `videoFrameUrls` are CLEARED entirely on the reference — the post still appears in the citation list (because text/visual hits also contributed via RRF), but no transcript context AND no segment-frame is surfaced. Better to omit weak signals than mislead downstream stages.

#### Step 2.1.d — Text rerank ★ (NEW)

Reranks the fused references against the topic query:

```
Text rerank rank 1/3  score=0.74  src=text+visual+video  postId=Post B
                              caption="Hands-on DJI Osmo Mobile 7 Pro… | transcript: …ActiveTrack…"
Text rerank rank 2/3  score=0.21  src=text+visual         postId=Post A
                              caption="Meai - Digital Camera đã cập nhật ảnh đại diện…"
Text rerank rank 3/3  score=0.05  src=text                postId=profile

Text rerank kept 2/3 refs (threshold=0.20, cap=10)
```

Post A barely makes it (0.21 ≥ 0.20). Profile doc (0.05) — **dropped from references** for the LLM context (still injected separately as the `=== Page profile ===` block; it's just removed from the fused-references citation list, which is appropriate since profile isn't a citable past post).

#### Step 2.1.e — Recommendation LLM call

Inputs (one chat-completion):
- **System prompt**: content strategist, brand-anchor, language detection, contact verbatim, web_search tool available
- **User text** (~25KB after rerank, was ~30KB before):
  - `=== Page profile ===` block (verbatim)
  - `=== Platform formula mapping ===` (BAB, AIDA, FAB, 3S, 5 Objections)
  - `=== Content guidance ===` (semantic knowledge — viral hooks + branded design + engagement triggers)
  - `=== Retrieved context from past posts ===` (LightRAG text)
  - `=== Top retrieved post references ===` — now just 2 entries (Post B first, Post A second), with Post B's segment time/transcript = `00:00:11` (the ActiveTrack one)
- **Reference images** (up to 4): from RAG visual hits
- **Tool**: `web_search(query)` — LLM may call to fetch fresh trends (e.g. "DJI Osmo Mobile 7 Pro release notes")

Output: structured recommendation in Vietnamese.

#### Step 2.1.f — Caption LLM call

Style-aware (`branded`), plain-text only, contact info verbatim from `=== Page profile ===` block, page-language enforced. Output: the actual Facebook-ready caption.

#### Step 2.1.g — Brave image search

Topic extracted: `"DJI Osmo Mobile 7 Pro"`. Brave returns 2 fresh-topic image URLs.

#### Step 2.1.h — Image rerank ★

Candidate pool — past-post posts now contribute **multiple candidates each** (the static image / thumbnail PLUS one frame URL per surviving video segment):

```
[fresh #1]                    https://...real-osmo-product-shot.jpg     ← Brave
[fresh #2]                    https://...another-osmo-photo.jpg         ← Brave
[past-post]                   s3://.../post-B-thumb.jpg                 ← Post B thumbnail
                                                                          (image_native ingest)
[past-post-video-frame]       s3://.../seg_03_frame_03.jpg              ← Post B segment 00:00:11
                                                                          frame from frame-level Qdrant
[past-post-video-frame]       s3://.../seg_01_frame_02.jpg              ← Post B segment 00:00:05
                                                                          frame from frame-level Qdrant
[past-post]                   s3://.../post-A-logo.jpg                  ← Post A
```

Jina rerank (model `jina-reranker-m0`, **fetches and analyzes the actual pixels** of each URL):

```
rank 1  score=0.91  past-post-video-frame  seg_03_frame_03   (gimbal close-up doing ActiveTrack — best subject match!)
rank 2  score=0.88  fresh #1                                 (real DJI Osmo product photo)
rank 3  score=0.72  past-post-video-frame  seg_01_frame_02   (gimbal in hand, intro shot)
rank 4  score=0.69  fresh #2
rank 5  score=0.41  past-post              post-B-thumb      (FB-generated thumbnail — title card overlay reduces visual relevance)
rank 6  score=0.32  past-post              post-A-logo       (MeAI logo, off-topic)  ← DROPPED below 0.40

Image rerank kept 5/6 (threshold=0.40, cap=4, dropped 2)
                  ↑ takes top 4 → frame_03, fresh#1, frame_02, fresh#2
```

**Key consequence of frame-level RAG** — Post B's *actual visually-relevant frame* (the close-up moment of the gimbal during ActiveTrack at `00:00:11`) outranks the FB-generated thumbnail. Pre-frame-level, the thumbnail was the only Post B contribution. Now image-gen sees the actual subject in motion as a reference, which is much closer to what the user wants generated.

Post A's logo is correctly **excluded from image-gen references** because pixel-wise it doesn't match a "DJI Osmo gimbal" topic — even though caption-text rerank kept it.

#### Step 2.1.i — Style knowledge + Image-brief LLM

Style knowledge for `branded` is fetched (composition rules, palette guidance). Image-brief LLM (gpt-4o-mini) takes:
- Caption, page profile, RAG summary, style knowledge
- 3 reference images (Step 2.1.h output)

Outputs strict JSON `{prompt, style_notes, aspect_ratio}`.

#### Step 2.1.j — Image generation

`gpt-5.4-image-2` receives the brief's prompt + style-aware system prompt + the same 3 reference images. Outputs a 1024×1024 PNG (~$0.234).

#### Step 2.1.k — S3 upload + persist

User-microservice gRPC stores the image; AI-microservice creates the `posts` row (status=draft); RabbitMQ publishes the completion notification.

---

## 3. The three rerank passes — at a glance

| | **Pass 1: Video-segment rerank** | **Pass 2: Text rerank** | **Pass 3: Image rerank** |
|---|---|---|---|
| Where | Inside `QueryAccountRecommendationsQuery` | Inside `QueryAccountRecommendationsQuery` | Inside `DraftPostGenerationConsumer` |
| When (in flow) | Right after `FuseReferences` | Right after segment rerank | Between caption gen and image-brief gen |
| Input | Multiple segments of the same video post | All fused references | Past-post images + fresh-topic Brave images |
| Document type sent to Jina | `{"text": transcript}` | `{"text": caption + " \| transcript: " + transcript}` | `{"image": url}` |
| What's gained | Best moment per video shown to LLM (was: "first match wins") | Token savings + sharper LLM focus + irrelevant refs dropped | Visually-relevant images for image-gen (drops off-topic visuals) |
| Threshold / cap | None — always picks the best segment | `0.20` / cap `10` | `0.40` / cap `request.maxReferenceImages` (≤8) |
| Failure mode | Logs warning, keeps original segment | Logs warning, returns RRF order | Logs warning, returns un-reranked order |

All three use the same Jina-reranker-m0 backend via `IRerankClient`. Each is ~1–4s wall-clock and ~$0.001 cost.

---

## 4. Data flow contract per stage

```
DraftPostTask           ──── correlation_id, style, isAutoTopic, userPrompt, maxReferenceImages, ...
       │
       ▼
GenerateDraftPostStarted (RabbitMQ)
       │
       ▼
DraftPostGenerationConsumer
       │
       ├── IndexSocialAccountPostsCommand ────── synchronous gRPC ingest
       │
       ├── QueryAccountRecommendationsQuery
       │     │
       │     ├── 4-leg parallel RAG (rag.Video now returns per-segment hits with `frame_url` from frame-level Qdrant)
       │     ├── FuseReferences (RRF)  → references carry `VideoFrameUrls` (one URL per matched segment)
       │     ├── PickBestVideoSegmentsAsync (Jina text rerank)  → narrows VideoFrameUrls to surviving segments
       │     ├── RerankReferencesByTextAsync (Jina text rerank)
       │     └── Recommendation LLM (gpt-4o-mini multimodal)
       │           Output: AccountRecommendationsAnswer { Answer, References (with VideoFrameUrls), WebSources, PageProfileText }
       │
       ├── BuildCaptionUserText + Caption LLM (gpt-4o-mini)
       │
       ├── ExtractRefImageQuery → Brave image search → freshRefImageHits
       │
       ├── Build candidate pool: per-post static image + N video frame URLs + fresh-topic
       │
       ├── SelectReferenceImagesAsync (Jina multimodal image rerank)  ← image-only docs
       │     Output: imageBriefRefImageUrls (≤8) — may include actual video frame(s)
       │     instead of (or in addition to) the static thumbnail
       │
       ├── FetchStyleKnowledgeAsync (knowledge:image-design-{style}:)
       │
       ├── BuildImageBriefAsync (gpt-4o-mini → JSON brief)
       │
       ├── ImageGenClient.GenerateImageAsync (gpt-5.4-image-2 → PNG data URL)
       │
       ├── UserResourceService gRPC → S3 upload → resource_id + presigned URL
       │
       └── PostRepository.AddAsync (Posts row, status=draft) + NotificationRequestedEvent
```

---

## 5. Cost + latency breakdown for a typical draft

| Stage | Wall clock | Cost |
|---|---:|---:|
| Indexing (cached, no new posts) | 2–6 s | ~$0 |
| Indexing (1 new image post) | 5–15 s | ~$0.005 |
| Indexing (1 new video post) | 35–200 s | ~$0.06 (Whisper + Gemini multimodal — per-frame embedding ~5× the embed cost vs old per-segment, but batched into one call per segment so wall-clock is comparable) |
| RAG retrieval (4 legs parallel) | 2–8 s | $0 |
| **Video-segment rerank** *(new)* | 1–2 s | ~$0.001 (Jina free tier covers most) |
| **Text rerank** *(new)* | 1–2 s | ~$0.001 |
| Recommendation LLM | 5–30 s | ~$0.005 |
| Caption LLM | 3–10 s | ~$0.001 |
| Brave image search | 0.3–0.8 s | ~$0.003 |
| **Image rerank** *(existing)* | 1–4 s | ~$0.001 |
| Style knowledge fetch | 0.5–2 s | $0 |
| Image-brief LLM | 3–10 s | ~$0.001 |
| **Image generation** | 30–180 s | **$0.234** |
| S3 upload + DB persist | 1–2 s | $0 |
| **Total** | **1–5 min** | **~$0.25** |

Image-gen is ~93% of cost; the three rerank passes together add ~$0.003 — under 2% of total spend.

---

## 6. Net effect of the new rerank passes on the two example posts

| | Before rerank passes + segment-level Qdrant | After (rerank passes + frame-level Qdrant) |
|---|---|---|
| Post B's segment(s) surfaced to recommendation LLM | First-matched only (segment `00:00:05` — generic intro), regardless of relevance | **Top-2 by relevance, threshold-gated**: `00:00:11` (ActiveTrack) + `00:00:05` (product intro), concatenated. Off-topic segments (intro/outro) dropped below 0.25 |
| LLM context when video has zero on-topic segments | First segment's transcript surfaced regardless — could mislead | **Segment + frame fields cleared** — no transcript surfaced, no frame URLs; post still appears via text/visual signal |
| Recommendation LLM user-text size | ~30 KB (all RRF-fused refs) | ~25 KB (irrelevant refs dropped) |
| Post A's logo seen as image-gen reference | Yes — top-K greedy | **No** — pixel rerank drops it (off-topic visual) |
| Post B's reference for image-gen | **Static thumbnail only** (image_native ingest of FB-generated thumb — often a title-card with text overlay) | **Best video frame(s) of relevant segment(s)** — the actual close-up of the gimbal during ActiveTrack at `00:00:11`, plus optionally the intro frame at `00:00:05`. Image-gen now anchors on real product motion, not a static thumbnail |
| Brave fresh DJI Osmo product shot seen | Yes | **Yes** — top score |
| Qdrant rows for Post B's video | ~6 (one per segment) | **~30** (5 frames × 6 segments) — 5× rows, but enables true frame-level retrieval. Storage cost ~5× per video; embedding cost ~5× at ingest (mitigated by batched API calls) |

**Outcome**: the generated image is now anchored on actual gimbal photos (real product + brand's previous gimbal-in-hand thumbnail) instead of being polluted by the brand-logo photo. The recommendation LLM cites the most informative moment of the brand's existing video. Token spend on the LLM is lower. All ~$0.003 / draft extra. Production is now genuinely subject-aware.
