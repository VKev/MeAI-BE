"""Restore baked knowledge into the live Qdrant + LightRAG state on startup.

Companion to ``bake_knowledge.py``. Runs from ``entrypoint.py`` in two phases:

Phase A — ``apply_filesystem_seed_if_needed``
  Must run BEFORE LightRAG's ``initialize_async`` so it sees the populated
  workspace state (kv_store_*.json + graphml) when it loads. Also merges the
  ``knowledge:*`` fingerprints into ``ingested_ids.json`` so subsequent
  bootstrap runs ack baked sections as ``skipped`` rather than re-extracting.

Phase B — ``apply_qdrant_seed_if_needed``
  Must run AFTER LightRAG's ``initialize_async`` so the collections exist with
  the right payload indexes. Upserts every baked point into the live Qdrant.

Idempotent: a marker file ``WORKING_DIR/.knowledge-seed-applied`` is written
with the manifest's content hash. If it already matches the seed's hash,
restoration is skipped. If the seed has been updated (new bake committed),
the hash differs → seed is reapplied (filesystem files are overwritten,
Qdrant points are upserted — same id replaces).

If no seed is present (e.g. dev compose with ``build:`` and an empty
``src/bakedknowledge/`` directory), every call is a no-op.

No LLM calls anywhere in this module.
"""
from __future__ import annotations

import json
import logging
import shutil
from pathlib import Path

from qdrant_client import AsyncQdrantClient
from qdrant_client.models import Distance, PointStruct, VectorParams

logger = logging.getLogger("rag-service.seed-loader")

# Where the bake artifacts land in the production image. Matches the
# COPY in dockerfile (src/ is copied to /app/src/, so bakedknowledge/ comes
# along by default — no Dockerfile change needed).
SEED_DIR_DEFAULT = Path("/app/src/bakedknowledge")

# Same constants as in bake_knowledge.py — duplicated here so seed_loader can
# stand alone without depending on the bake module (which imports the full
# container DI graph). Keep these two lists in sync if either changes.
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


def _seed_present(seed_dir: Path) -> dict | None:
    """Return the manifest dict if a usable seed lives at `seed_dir`, else None."""
    manifest_path = seed_dir / "manifest.json"
    if not manifest_path.exists():
        return None
    try:
        return json.loads(manifest_path.read_text(encoding="utf-8"))
    except Exception as ex:  # noqa: BLE001
        logger.warning("Seed manifest at %s is unreadable: %s", manifest_path, ex)
        return None


def _read_marker(workdir: Path) -> str | None:
    """The marker holds the manifest's content_hash from the bake that was last
    applied. None means "never applied" (or wiped volume)."""
    marker = workdir / ".knowledge-seed-applied"
    if not marker.exists():
        return None
    try:
        return marker.read_text(encoding="utf-8").strip() or None
    except Exception:
        return None


def _write_marker(workdir: Path, content_hash: str) -> None:
    (workdir / ".knowledge-seed-applied").write_text(content_hash, encoding="utf-8")


def apply_filesystem_seed_if_needed(
    *,
    working_dir: str | Path,
    qdrant_namespace: str,
    seed_dir: str | Path = SEED_DIR_DEFAULT,
) -> dict | None:
    """Phase A. Returns the manifest if seed was applied, None if skipped.
    The returned manifest is consumed by `apply_qdrant_seed_if_needed`."""
    seed = Path(seed_dir)
    workdir = Path(working_dir)

    manifest = _seed_present(seed)
    if manifest is None:
        logger.info("No baked knowledge seed at %s — skipping", seed)
        return None

    content_hash = manifest.get("knowledge_content_hash") or ""
    last_applied = _read_marker(workdir)
    if last_applied == content_hash and content_hash:
        logger.info(
            "Knowledge seed already applied (hash=%s) — skipping filesystem restore",
            content_hash,
        )
        return None

    if last_applied and last_applied != content_hash:
        logger.info(
            "Seed hash changed (was=%s, now=%s) — reapplying",
            last_applied, content_hash,
        )

    # Copy LightRAG state files into the workspace dir.
    rag_state = seed / "rag_state"
    if not rag_state.exists():
        logger.warning("Seed at %s has no rag_state/ — skipping filesystem restore", seed)
        return None

    namespace_dir = workdir / qdrant_namespace
    namespace_dir.mkdir(parents=True, exist_ok=True)

    copied = 0
    for fname in LIGHTRAG_STATE_FILES:
        src = rag_state / fname
        if not src.exists():
            continue
        dst = namespace_dir / fname
        # Overwrite — if seed hash changed, we want the new state to win.
        shutil.copy2(src, dst)
        copied += 1
    logger.info("Restored %d LightRAG state files from seed → %s", copied, namespace_dir)

    # Merge knowledge fingerprints into the runtime registry. Preserve any
    # non-knowledge entries already there (e.g. previously-ingested FB posts).
    seed_registry_path = seed / "ingested_ids.knowledge.json"
    registry_path = workdir / "ingested_ids.json"
    if seed_registry_path.exists():
        seed_registry: dict[str, str] = json.loads(seed_registry_path.read_text(encoding="utf-8"))
        existing: dict[str, str] = {}
        if registry_path.exists():
            try:
                existing = json.loads(registry_path.read_text(encoding="utf-8"))
            except Exception:
                existing = {}
        existing.update(seed_registry)  # seed entries replace existing knowledge:*
        registry_path.write_text(
            json.dumps(existing, indent=2, sort_keys=True),
            encoding="utf-8",
        )
        logger.info(
            "Merged %d knowledge fingerprints into runtime registry",
            len(seed_registry),
        )

    return manifest


async def apply_qdrant_seed_if_needed(
    *,
    qdrant_url: str,
    qdrant_api_key: str | None,
    working_dir: str | Path,
    manifest: dict,
    seed_dir: str | Path = SEED_DIR_DEFAULT,
) -> bool:
    """Phase B. Upsert baked Qdrant points into the live Qdrant. `manifest` is
    the dict returned by Phase A; pass None to skip (Phase A already declined).

    Idempotent: same-id upserts replace; the marker file at the end gates a
    fast-path skip on subsequent boots.
    """
    seed = Path(seed_dir)
    qdrant_points_dir = seed / "qdrant_points"
    if not qdrant_points_dir.exists():
        logger.warning("Seed has no qdrant_points/ — skipping qdrant restore")
        return False

    content_hash = manifest.get("knowledge_content_hash") or ""

    qc = AsyncQdrantClient(url=qdrant_url, api_key=qdrant_api_key)
    try:
        for points_file in sorted(qdrant_points_dir.glob("*.json")):
            blob = json.loads(points_file.read_text(encoding="utf-8"))
            await _restore_collection(qc, blob)
    finally:
        await qc.close()

    _write_marker(Path(working_dir), content_hash)
    logger.info("Knowledge seed applied (hash=%s); marker updated", content_hash)
    return True


async def _restore_collection(qc: AsyncQdrantClient, blob: dict) -> None:
    collection: str = blob["collection"]
    points_count: int = blob.get("points_count", 0)
    if points_count == 0:
        logger.info("Collection %s in seed has 0 points — skipping", collection)
        return

    # Make sure the collection exists. LightRAG should have created it during
    # initialize_async, but if for some reason it didn't (e.g. it failed
    # silently or this is a fresh Qdrant), we create it here from the seed's
    # vectors_config.
    try:
        await qc.get_collection(collection)
    except Exception:  # noqa: BLE001
        vc = blob.get("vectors_config", {})
        size = vc.get("size") or 1536
        distance_str = (vc.get("distance") or "Cosine").split(".")[-1].lower()
        distance = Distance.COSINE
        if "euclid" in distance_str:
            distance = Distance.EUCLID
        elif "dot" in distance_str:
            distance = Distance.DOT
        await qc.create_collection(
            collection_name=collection,
            vectors_config=VectorParams(size=size, distance=distance),
        )
        logger.info("Created missing collection %s (size=%d distance=%s)", collection, size, distance)

    # Upsert in batches.
    points = blob["points"]
    batch_size = 100
    for i in range(0, len(points), batch_size):
        batch = points[i:i + batch_size]
        structs = [
            PointStruct(
                id=p["id"],
                vector=p["vector"],
                payload=p.get("payload") or {},
            )
            for p in batch
            if p.get("vector") is not None
        ]
        if not structs:
            continue
        await qc.upsert(collection_name=collection, points=structs)
    logger.info("Upserted %d points into %s", len(points), collection)
