# Baked knowledge artifacts

This directory holds the **result of running knowledge bootstrap once**,
packaged so production rag-microservice containers can restore the knowledge
base on cold start **without making any LLM calls**.

The artifacts here are committed to git. They are regenerated only when you
edit / add / remove files under `../knowledge/`.

## What lives here

| File / dir | What it is |
|---|---|
| `manifest.json` | Bake metadata — timestamp, embedding model + dim, content hash of the bake's input markdown files. The hash gates whether the seed loader skips or re-applies on container start. |
| `rag_state/` | LightRAG filesystem state: `kv_store_*.json` (full docs / chunks / entities / relations / doc_status / LLM-response cache) plus `graph_chunk_entity_relation.graphml`. Restored to `WORKING_DIR/<workspace>/`. |
| `qdrant_points/<collection>.json` | One file per LightRAG vector collection (`lightrag_vdb_chunks` / `lightrag_vdb_entities` / `lightrag_vdb_relationships`). Each file is `{collection, vectors_config, points_count, points: [{id, vector, payload}]}`. Knowledge slice only — the bake runs against a **disposable Qdrant** with no FB account data ever written to it, so by construction nothing private can leak in. |
| `ingested_ids.knowledge.json` | Fingerprint registry filtered to only `knowledge:*` document ids. Merged into the runtime `ingested_ids.json` so subsequent bootstrap runs ack baked sections as `skipped` rather than re-extracting. |

(This file lives at `Rag.Microservice/src/bakedknowledge/` — it's inside `src/`
so the existing `COPY Rag.Microservice/src /app/src` in the dockerfile picks
it up automatically. No Dockerfile change is needed.)

## How to (re)bake

When you edit `../knowledge/*.md`:

```bash
# From Backend/Microservices/Rag.Microservice/
export LLM_API_KEY=sk-or-v1-your-openrouter-key
./bake.sh
```

The bake spawns a **disposable empty Qdrant + a one-shot rag-microservice**
via `bake.compose.yml`. It runs the bootstrap once, writes artifacts here,
and tears everything down.

After it completes:

```bash
git status .
git diff manifest.json   # quickest way to see whether anything changed
git add .
git commit -m "rebake knowledge"
git push
```

## What happens on production container start

`src/composition/seed_loader.py`:

1. Reads `manifest.json`. If `WORKING_DIR/.knowledge-seed-applied` already
   contains the same `knowledge_content_hash`, skip everything — already
   restored.
2. **Phase A** (before LightRAG initializes): copy `rag_state/*` into
   `WORKING_DIR/<workspace>/`, merge `ingested_ids.knowledge.json` into the
   runtime registry.
3. LightRAG initializes (creates Qdrant collections with payload indexes).
4. **Phase B** (after init): upsert every point from `qdrant_points/*.json`
   into the live Qdrant. Same-id upserts replace, so this is idempotent.
5. Write the new content hash into the marker.

**No embedding calls. No entity-extraction LLM calls. ~5-15s end to end.**

## Per-account FB ingest is unaffected

The seed loader only touches the `knowledge:*` document slice. Any
`facebook:<social-media-id>:*` documents already in the volume / Qdrant from
prior `/index` calls are preserved — the runtime fingerprint registry is
merged, not overwritten.

## Notes / caveats

- **Vector dim must match between bake and runtime.** If you change
  `EMBED_MODEL` or `EMBED_DIM`, you must rebake; otherwise the live Qdrant
  collection (created with the new dim) will reject the old vectors.
- **Section-deletion orphans**: if you delete a `## Section` from a `.md`
  file and rebake, that section's chunks/entities are no longer in the new
  bake — but if a previous bake had been applied, they're still in the live
  Qdrant after restore. The marker file gates re-application so on cold
  volumes the old data is naturally gone; on warm volumes you'd need to
  clean up by hand. Future enhancement: have the seed loader scrub
  `knowledge:*` points whose ids aren't in the new bake.
- **Don't commit anything else here.** This directory is mounted RW by the
  bake script — extra files would survive the bake and ship in the image.
