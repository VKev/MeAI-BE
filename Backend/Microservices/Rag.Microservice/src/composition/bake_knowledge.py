"""One-shot knowledge bake. Run inside the bake stack (see ``bake.compose.yml``).

Connects to a disposable Qdrant (started by docker compose), runs
``KnowledgeBootstrapService.bootstrap()`` once against ``src/knowledge/*.md``,
then exports the resulting state to ``--output`` (default
``/app/src/bakedknowledge``, which the bake compose mounts from the host's
``Backend/Microservices/Rag.Microservice/src/bakedknowledge/``).

Outputs:
  - ``manifest.json`` — bake metadata (timestamp, embed model, dim, content hash)
  - ``rag_state/<file>`` — LightRAG kv_store_*.json + graphml from the workspace dir
  - ``qdrant_points/<collection>.json`` — every point in each LightRAG text vector
    collection, dumped as ``{id, vector, payload}`` triples
  - ``ingested_ids.knowledge.json`` — fingerprint registry filtered to
    ``knowledge:*`` doc ids only (per-account FB entries can't slip in because
    the bake's Qdrant is empty by construction)

Restoration on production container start happens in ``seed_loader.py``.

Run via ``./bake.sh`` from ``Backend/Microservices/Rag.Microservice/``.
"""
from __future__ import annotations

import argparse
import asyncio
import hashlib
import json
import logging
import os
import shutil
import sys
from datetime import datetime, timezone
from pathlib import Path

from qdrant_client import AsyncQdrantClient

from .config import load_config
from .container import build_container, close_async, initialize_async

logger = logging.getLogger("rag-service.bake")

# LightRAG state files we save. Anything else under the workspace is either
# a transient cache or recreated at startup.
LIGHTRAG_STATE_FILES = (
    "kv_store_full_docs.json",
    "kv_store_text_chunks.json",
    "kv_store_full_entities.json",
    "kv_store_full_relations.json",
    "kv_store_entity_chunks.json",
    "kv_store_relation_chunks.json",
    "kv_store_doc_status.json",
    "kv_store_llm_response_cache.json",
    "graph_chunk_entity_relation.graphml",
)

# Qdrant collections containing the LightRAG knowledge text vectors. Multimodal
# (`meai_rag_visual_v2`) and video (`meai_rag_video_segments`) collections are
# per-account FB data only, never knowledge — skipped by design.
KNOWLEDGE_COLLECTIONS = (
    "lightrag_vdb_chunks",
    "lightrag_vdb_entities",
    "lightrag_vdb_relationships",
)


async def main(output_dir: Path) -> int:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(name)s | %(message)s",
    )

    cfg = load_config()
    logger.info("Bake → %s", output_dir)
    logger.info("Working dir: %s", cfg.working_dir)
    logger.info("Qdrant URL:  %s (namespace=%s)", cfg.qdrant_url, cfg.qdrant_namespace)
    logger.info("Embed model: %s (dim=%d)", cfg.embed_model, cfg.embed_dim)
    logger.info("Knowledge dir: %s", cfg.knowledge_dir)

    # The bake compose mounts a fresh tmp WORKING_DIR — but make sure.
    workdir = Path(cfg.working_dir)
    namespace_dir = workdir / cfg.qdrant_namespace
    if namespace_dir.exists() and any(namespace_dir.iterdir()):
        logger.warning(
            "Working dir %s is not empty — bake will use existing state as starting point",
            namespace_dir,
        )

    # ── Run the bootstrap (this is the only step that costs LLM tokens) ──
    container = build_container(cfg)
    await initialize_async(container)
    logger.info("Running knowledge bootstrap...")
    counts = await container.knowledge_bootstrap.bootstrap()
    total = sum(counts.values())
    logger.info("Bootstrap complete: %d new/updated docs across %d namespaces", total, len(counts))
    for ns, n in sorted(counts.items()):
        logger.info("  %s: %d docs", ns, n)

    # ── Export ───────────────────────────────────────────────────────────
    output_dir.mkdir(parents=True, exist_ok=True)
    rag_state_out = output_dir / "rag_state"
    qdrant_points_out = output_dir / "qdrant_points"
    if rag_state_out.exists():
        shutil.rmtree(rag_state_out)
    if qdrant_points_out.exists():
        shutil.rmtree(qdrant_points_out)
    rag_state_out.mkdir(parents=True)
    qdrant_points_out.mkdir(parents=True)

    # 1. Qdrant points — scroll each LightRAG text collection
    qc = AsyncQdrantClient(url=cfg.qdrant_url, api_key=cfg.qdrant_api_key)
    try:
        for collection in KNOWLEDGE_COLLECTIONS:
            await _export_collection(qc, collection, qdrant_points_out)
    finally:
        await qc.close()

    # 2. LightRAG filesystem state — copy from <workdir>/<workspace>/
    state_src = namespace_dir if namespace_dir.exists() else workdir
    copied = 0
    for fname in LIGHTRAG_STATE_FILES:
        src = state_src / fname
        if not src.exists():
            logger.warning("state file missing: %s", src)
            continue
        dst = rag_state_out / fname
        shutil.copy2(src, dst)
        copied += 1
        logger.info("state file copied: %s (%d bytes)", fname, dst.stat().st_size)
    logger.info("Copied %d LightRAG state files", copied)

    # 3. Fingerprint registry — keep only knowledge:* entries
    full_registry: dict[str, str] = {}
    registry_path = workdir / "ingested_ids.json"
    if registry_path.exists():
        full_registry = json.loads(registry_path.read_text(encoding="utf-8"))
    knowledge_registry = {
        doc_id: fp
        for doc_id, fp in full_registry.items()
        if doc_id.startswith("knowledge:")
    }
    (output_dir / "ingested_ids.knowledge.json").write_text(
        json.dumps(knowledge_registry, indent=2, sort_keys=True),
        encoding="utf-8",
    )
    logger.info("Wrote %d knowledge fingerprints", len(knowledge_registry))

    # 4. Manifest
    knowledge_dir = Path(cfg.knowledge_dir)
    md_files = sorted(knowledge_dir.glob("*.md"))
    content_hash = hashlib.sha256(
        b"".join(f.read_bytes() for f in md_files)
    ).hexdigest()[:16]

    manifest = {
        "version": "1.0",
        "baked_at_utc": datetime.now(timezone.utc).isoformat(),
        "embed_model": cfg.embed_model,
        "embed_dim": cfg.embed_dim,
        "qdrant_namespace": cfg.qdrant_namespace,
        "knowledge_content_hash": content_hash,
        "knowledge_files": [f.name for f in md_files],
        "doc_counts_by_namespace": counts,
        "qdrant_collections": list(KNOWLEDGE_COLLECTIONS),
        "fingerprint_count": len(knowledge_registry),
    }
    (output_dir / "manifest.json").write_text(
        json.dumps(manifest, indent=2),
        encoding="utf-8",
    )
    logger.info("Wrote manifest (content_hash=%s)", content_hash)

    await close_async(container)
    logger.info("Bake complete. git diff %s to review.", output_dir)
    return 0


async def _export_collection(
    qc: AsyncQdrantClient,
    collection: str,
    out_dir: Path,
) -> None:
    """Scroll all points in `collection` and dump to JSON. Disposable Qdrant
    (empty at start, never sees FB data) means we don't need a payload filter
    here — by construction every point is a knowledge point."""
    try:
        info = await qc.get_collection(collection)
    except Exception as ex:  # noqa: BLE001
        logger.warning("Collection %s unavailable, skipping export: %s", collection, ex)
        return

    vc = info.config.params.vectors
    # Handle both unnamed and named vectors. We use unnamed everywhere.
    vector_size = getattr(vc, "size", None)
    distance = str(getattr(vc, "distance", "Cosine"))
    if vector_size is None:
        # named vectors map — pick the first
        first = next(iter(vc.values())) if hasattr(vc, "values") else None
        vector_size = getattr(first, "size", None)
        distance = str(getattr(first, "distance", "Cosine"))

    all_points: list[dict] = []
    next_offset = None
    while True:
        points, next_offset = await qc.scroll(
            collection_name=collection,
            limit=200,
            offset=next_offset,
            with_payload=True,
            with_vectors=True,
        )
        for p in points:
            vec = p.vector
            # Some lightrag setups use named vectors → vec is a dict
            if isinstance(vec, dict):
                vec = next(iter(vec.values()), None)
            all_points.append({
                "id": _id_to_jsonable(p.id),
                "vector": list(vec) if vec is not None else None,
                "payload": p.payload or {},
            })
        if next_offset is None:
            break

    blob = {
        "collection": collection,
        "vectors_config": {"size": vector_size, "distance": distance},
        "points_count": len(all_points),
        "points": all_points,
    }
    out_file = out_dir / f"{collection}.json"
    out_file.write_text(json.dumps(blob, indent=None, separators=(",", ":")), encoding="utf-8")
    logger.info(
        "Exported %d points from %s → %s (%.1f KB)",
        len(all_points), collection, out_file.name,
        out_file.stat().st_size / 1024.0,
    )


def _id_to_jsonable(point_id):
    """Qdrant point ids may be int or UUID — keep as-is for JSON."""
    if isinstance(point_id, (int, str)):
        return point_id
    return str(point_id)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Bake the knowledge base for restore-on-start.")
    parser.add_argument(
        "--output",
        default=os.environ.get("BAKE_OUTPUT", "/app/src/bakedknowledge"),
        help="Output directory for baked artifacts (mounted from host at bake time).",
    )
    args = parser.parse_args()
    sys.exit(asyncio.run(main(Path(args.output))))
