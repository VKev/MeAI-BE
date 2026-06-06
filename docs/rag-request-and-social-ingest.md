# Luong RAG va ingest bai dang social account

Tai lieu nay giai thich cach RAG cua backend MeAI hoat dong tu luc co request cho den luc query vector DB, va cach ingest ban dau cac bai dang tu social account.

## 1. Thanh phan chinh

RAG trong MeAI khong phai la mot HTTP API public. `Rag.Microservice` chi mo `/health` cho health check; traffic that su di qua:

- **Ai.Microservice**: service .NET dieu phoi request tu FE/API, goi ingest/query RAG, sau do goi LLM de viet ket qua.
- **Rag.Microservice**: service Python chua LightRAG, visual search, VideoRAG.
- **RabbitMQ**:
  - `meai.rag.query`: RPC query, co reply queue + correlation id.
  - `meai.rag.ingest`: ingest fire-and-forget cu, hien tai nhung flow can ket qua ngay uu tien gRPC.
- **gRPC**:
  - `RagIngestService/IngestBatch`: ingest dong bo. Ai service chi tiep tuc khi RAG da embed/upsert xong.
- **Qdrant**:
  - Vector DB chinh cho text, visual, video.
- **S3 mirror**:
  - Dung cho anh social/CDN vi provider AI thuong khong fetch duoc URL Facebook/Instagram/TikTok truc tiep.

## 2. Query RAG tu mot request AI

Vi du cac flow nhu AI Recommendation, draft post, improve post, account analysis deu co mau chung:

1. FE goi endpoint cua `Ai.Microservice`.
2. Ai handler/consumer goi `WaitForRagReadyAsync`.
3. `WaitForRagReadyAsync` gui RPC `{ op: "wait_ready" }` vao `meai.rag.query`.
4. `Rag.Microservice` goi `LazyKnowledgeBootstrap.ensure_ready()`.
5. Neu baked knowledge da restore xong thi tra ve ngay; neu chua thi request dau tien se doi bootstrap knowledge.
6. Ai service index/refresh social account neu flow can context moi.
7. Ai service tao query RAG, thuong qua `IQueryRewriter` de tach `primary_query`, `alt_queries`, `visual_query`, `key_terms`.
8. Ai service goi `MultimodalQueryAsync`.

Payload multimodal query co dang logic:

```json
{
  "op": "multimodal_query",
  "query": "primary query da rewrite",
  "documentIdPrefix": "facebook:<socialMediaIdN>:",
  "topK": 8,
  "modes": ["text", "visual", "video"],
  "platform": "facebook",
  "socialMediaId": "<guid N format>"
}
```

`documentIdPrefix` la cach scope theo account. Moi document cua account do co prefix:

```text
{platform}:{socialMediaId.N}:
```

Vi du:

```text
facebook:019e8e56d3647fec9dc796dd37de9854:
```

## 3. Ben trong `Rag.Microservice` khi query

`QueryService.multimodal_query()` chay song song 3 leg: text, visual, video.

### 3.1 Text leg

Text leg dung LightRAG.

1. Lay danh sach fingerprint/document id khop `documentIdPrefix`.
2. Goi LightRAG `query_with_references(...)` voi:
   - `mode = "hybrid"`
   - `only_need_context = true`
   - `top_k = topK`
   - `ids = matched document ids`
3. `LightRagFacade` monkeypatch Qdrant query cua LightRAG de filter chunk theo `full_doc_id in allowlist`.
4. Ket qua tra ve:
   - context text tu past posts/profile/image descriptions.
   - matched document ids.
   - references co `documentId`, `postId`, `content`, `caption`.

Vector DB dung cho text la Qdrant qua LightRAG:

- `lightrag_vdb_chunks`
- `lightrag_vdb_entities`
- `lightrag_vdb_relationships`

Workspace/namespace production la:

```text
QDRANT_NAMESPACE = meai_rag
```

Embedding text production:

```text
openai/text-embedding-3-small, 1536 dim
```

LightRAG cung duoc wire Jina rerank cho text-mode retrieval neu `RERANK_API_KEY` co cau hinh.

### 3.2 Visual leg

Visual leg bypass LightRAG, dung Qdrant collection rieng cho anh.

1. Embed query text bang multimodal embedder.
2. Search Qdrant collection visual.
3. Filter payload:

```text
scope = {platform}:{socialMediaId.N}:
```

Production collection:

```text
meai_rag_visual_v2
```

Production embedding:

```text
google/gemini-embedding-2-preview, 3072 dim
```

Moi anh co the co 2 vector point:

- `kind=image`: vector cua anh.
- `kind=caption`: vector cua caption trong cung embedding space.

Payload Qdrant giu:

- `document_id`
- `kind`
- `scope`
- `image_url`
- `mirror_s3_key`
- `caption`
- `post_id`
- `fingerprint`

Khi query, `QdrantVisualStore.search()` tra ve `mirroredImageUrl` moi duoc presign tu S3 de LLM/OpenRouter co the fetch.

### 3.3 Video leg

Video leg dung `VideoRAG`.

1. Can `platform` va `socialMediaId` trong payload.
2. Tao scope hash tu:

```text
sha256("{platform}|{socialMediaId}")[:16]
```

3. Lay hoac tao VideoRAG instance theo scope.
4. Query video bang 2 leg:
   - visual segment/frame leg.
   - text transcript/chunk leg.
5. Fuse ket qua bang Reciprocal Rank Fusion trong VideoRAG adapter.
6. Hydrate segment de lay:
   - caption/content cua segment.
   - transcript.
   - timestamp.
   - postId.
   - frame URL neu co.

Production video vector collection:

```text
meai_rag_video_segments
```

Production dim:

```text
3072
```

VideoRAG cung co working directory rieng trong volume `rag-data`, dung de luu state segment/transcript/frame theo scope account.

## 4. Ai service lam gi sau khi RAG tra ve

Trong `QueryAccountRecommendationsQueryHandler`:

1. Goi account multimodal RAG:
   - text
   - visual
   - video
2. Goi them knowledge RAG text queries:
   - page profile: `{prefix}profile`
   - platform formula mapping: `knowledge:content-formulas:platform-mapping-{platform}`
   - semantic knowledge theo intent: formulas, hooks, engagement, design, algorithm.
3. Fuse references tu text/visual/video bang Reciprocal Rank Fusion.
4. Neu mot video post co nhieu matched segments, rerank transcript segment bang Jina va chi giu segment lien quan.
5. Rerank toan bo fused references bang text rerank:
   - input rerank la caption + transcript tot nhat.
   - threshold hien tai la `0.20`.
   - cap toi da `10` references.
6. Lay toi da `4` anh reference cho multimodal LLM, uu tien `MirroredImageUrl` hon raw social CDN URL.
7. Build prompt LLM gom:
   - user question.
   - target platform.
   - page profile.
   - optional content guidance.
   - retrieved context tu past posts.
   - top post references.
   - attached reference images.
8. Goi multimodal LLM de viet recommendation/final answer.

Nghia la RAG service chi retrieve context/reference. Viec hop nhat, rerank cuoi, va viet cau tra loi nam o `Ai.Microservice`.

## 5. Ingest ban dau bai dang tu social account

Ingest ban dau la lan dau `IndexSocialAccountPostsCommand` chay cho mot social account, hoac lan refresh khi co post moi/post thay doi.

Flow nay thuong duoc goi truoc draft post/recommendation/improve/account analysis de dam bao RAG co du lieu moi. No khong phai cron job bat buoc trong RAG; no la command cua `Ai.Microservice`.

### 5.1 Lay bai dang tu platform

`IndexSocialAccountPostsCommand` goi:

```text
GetSocialMediaPlatformPostsQuery
```

De lay post theo page:

- page size: `25`
- default max posts: `200`
- hard cap: `2000`

Neu fetch bi loi giua chung nhung da co mot so post, command van index phan da lay duoc.

### 5.2 Xac dinh prefix va fingerprint

Prefix account:

```text
{platform}:{socialMediaId.N}:
```

Truoc khi queue ingest, Ai service goi:

```text
ListFingerprintsAsync(prefix)
```

RAG service doc `ingested_ids.json` va tra ve document id + fingerprint da ingest.

Muc dich:

- post moi: ingest.
- post thay doi: update.
- post khong doi: skip.

Luu y: fingerprint text post khong tinh cac counter de tranh re-embed moi lan likes/views tang. Engagement metrics van nam trong content text doc de LLM doc, nhung khong lam trigger paid embedding lien tuc.

### 5.3 Cac document duoc tao cho moi account/post

| Loai | Document id | Kind | Noi dung/ket qua | Noi luu |
|---|---|---|---|---|
| Page profile | `{prefix}profile` | `text` | Facebook About, category, website, email, phone, location, bio. Hien tai rich profile moi co cho Facebook. | LightRAG/Qdrant text |
| Post text | `{prefix}{postId}` | `text` | Title, caption, description, media type, publishedAt, permalink, engagement metrics. | LightRAG/Qdrant text |
| Image description | `{prefix}{postId}:img:0` | `image` | Vision LLM mo ta anh, sau do chen nhu text doc. | LightRAG/Qdrant text |
| Image native | `{prefix}{postId}:vis2:0` | `image_native` | Mirror anh sang S3, embed anh + caption vao visual collection. | Qdrant `meai_rag_visual_v2` |
| Video | `{prefix}{postId}:vid:0` | `video` | Download video, split segment, frame/transcript/caption, embed segment/frame. | VideoRAG + Qdrant `meai_rag_video_segments` |

### 5.4 Text doc gom gi

Post text doc co dang:

```text
[Post <platformPostId> on <platform>]
Title: ...
Caption: ...
Description: ...
MediaType: ...
PublishedAt: ...
Permalink: ...
Engagement: 100 views - 10 likes - 3 comments ...
TotalInteractions: ...
```

Day la context de LightRAG tra ve khi user hoi ve noi dung, performance, angle, caption, format bai cu.

### 5.5 Image ingest co hai duong

Mot post co anh se ingest 2 kieu:

1. **`kind=image`**
   - Vision LLM mo ta anh.
   - Description duoc dua vao LightRAG nhu text.
   - Tot cho LLM doc hieu: subject, logo, OCR, mau sac, mood, style.

2. **`kind=image_native`**
   - Anh duoc mirror sang S3 truoc.
   - Embed image va caption bang multimodal embedding.
   - Upsert vao Qdrant visual collection.
   - Tot cho text-to-image retrieval: query text co the tim anh past post phu hop.

Ly do can mirror S3: URL CDN cua Facebook/Instagram/TikTok co the bi provider AI tu choi fetch. S3 presigned/public base URL on dinh hon cho OpenRouter/Kie/Gemini.

### 5.6 Video ingest

Chi chay khi post co `MediaType` la video/reel va co URL video fetch duoc.

VideoRAG:

- download video vao temp file.
- chia video thanh segment.
- lay frames theo segment.
- tao transcript/caption cho segment.
- embed visual/text signal.
- luu theo scope account.

Video ingest nang hon image rat nhieu nen code gate chat theo media type va URL.

### 5.7 Ingest dong bo qua gRPC

Sau khi tao danh sach docs, Ai service gui batch qua:

```text
RagIngestService/IngestBatch
```

Batch size hien tai:

```text
3 documents / batch
```

RAG service voi moi document:

1. `LazyKnowledgeBootstrap.ensure_ready()`.
2. `FingerprintRegistry.reconcile(documentId, fingerprint)`.
3. Neu unchanged thi tra ve `skipped`, gRPC map thanh `unchanged`.
4. Neu updated thi co gang delete doc cu trong LightRAG truoc.
5. Dispatch theo `kind`:
   - `text`
   - `image`
   - `image_native`
   - `video`
6. Ghi fingerprint moi vao `ingested_ids.json` neu ingest thanh cong.

Neu multimodal embedding fail toan bo cho `image_native`, fingerprint khong duoc record de lan index sau retry.

## 6. Knowledge RAG ban dau

Ngoai social account posts, RAG co global knowledge base tu:

```text
Backend/Microservices/Rag.Microservice/src/knowledge/*.md
```

Moi file markdown duoc parse theo section `##`, tao document id:

```text
knowledge:<namespace>:<slug>
```

Production thuong dung baked knowledge:

1. `src/bakedknowledge/` da chua LightRAG state + Qdrant points cua knowledge.
2. Khi container start, seed loader restore vao `rag-data` va Qdrant.
3. Neu marker hash khop, lazy bootstrap duoc mark ready va request dau tien khong ton LLM cost.
4. Neu khong co seed hoac hash mismatch, request dau tien se bootstrap knowledge bang `LazyKnowledgeBootstrap`.

Knowledge nay duoc query bo sung khi AI tao recommendation:

- content formulas
- viral hooks
- engagement triggers
- visual design
- platform algorithm signals
- image design theo style

Code treat knowledge nhu heuristic, khong phai su that bat buoc. LLM co the bo qua neu khong hop account/topic.

## 7. Tom tat duong di du lieu

### Initial ingest

```text
Social platform API
  -> Ai.Microservice GetSocialMediaPlatformPostsQuery
  -> IndexSocialAccountPostsCommand
  -> ListFingerprintsAsync(prefix) qua RabbitMQ RPC
  -> build text/image/image_native/video docs
  -> gRPC IngestBatch
  -> Rag.Microservice IngestService
  -> LightRAG/Qdrant visual/VideoRAG
  -> ingested_ids.json fingerprint registry
```

### Query luc tao recommendation

```text
FE request
  -> Ai.Microservice endpoint/consumer
  -> WaitForRagReadyAsync
  -> optional IndexSocialAccountPostsCommand
  -> IQueryRewriter
  -> MultimodalQueryAsync(prefix, modes text+visual+video)
  -> Rag.Microservice QueryService
       -> LightRAG text leg
       -> Qdrant visual leg
       -> VideoRAG video leg
  -> Ai.Microservice fuse RRF + Jina rerank
  -> build LLM prompt + attach S3 mirrored images
  -> multimodal LLM final answer/draft/analysis
```

## 8. Cac diem can nho khi debug

- Neu query ra rong, kiem tra prefix co dung `{platform}:{socialMediaId.N}:` khong.
- Neu text co context nhung anh khong co, kiem tra `image_native`, S3 mirror, va collection `meai_rag_visual_v2`.
- Neu video khong co hit, kiem tra post co direct video URL khong, `VIDEORAG_ENABLED`, va collection `meai_rag_video_segments`.
- Neu RAG ton cost lap lai, kiem tra fingerprint co bi thay doi vi URL/media khong on dinh khong.
- Neu LLM khong dung anh reference, nho la RAG chi retrieve candidate; viec chon/refine reference cuoi nam o Ai service rerank va prompt LLM.
- Neu knowledge bootstrap cham, kiem tra baked knowledge marker va volume `rag-data`/`qdrant-data`.

## 9. File code lien quan

- `Backend/Microservices/Ai.Microservice/src/Application/Recommendations/Commands/IndexSocialAccountPostsCommand.cs`
- `Backend/Microservices/Ai.Microservice/src/Application/Recommendations/Queries/QueryAccountRecommendationsQuery.cs`
- `Backend/Microservices/Ai.Microservice/src/Infrastructure/Logic/Rag/RabbitMqRagClient.cs`
- `Backend/Microservices/Rag.Microservice/src/application/services/ingest_service.py`
- `Backend/Microservices/Rag.Microservice/src/application/services/query_service.py`
- `Backend/Microservices/Rag.Microservice/src/infrastructure/lightrag_facade.py`
- `Backend/Microservices/Rag.Microservice/src/infrastructure/qdrant_visual_store.py`
- `Backend/Microservices/Rag.Microservice/src/infrastructure/video_rag/adapter.py`
- `Backend/Microservices/Rag.Microservice/src/transport/rabbit_consumer.py`
- `Backend/Microservices/Rag.Microservice/src/transport/grpc/rag_ingest_servicer.py`
