"""Qdrant-backed video FRAME vector storage.

Replaces NanoVectorDBVideoSegmentStorage when VIDEORAG_USE_QDRANT=1. One shared
Qdrant collection holds one row per FRAME (not per segment) across accounts;
per-scope isolation is via a `scope` payload field. Per-frame storage means image-gen
can later use the actually-relevant frame as a reference, not just the video's
static thumbnail — at the cost of ~5× more rows per video (one per sampled frame).

At query time, we cosine-rank frames; the engine layer (`videorag_engine.py`) then
groups frame hits by segment and surfaces the best-scoring frame's URL per segment.

The collection name comes from VIDEORAG_QDRANT_SEGMENT_COLLECTION (default
`meai_rag_video_frames`). Vectors are 3072-dim (Gemini Embedding 2 Preview).

Uses the synchronous QdrantClient because some upstream call sites (multiprocessing
workers) cannot rely on the parent's asyncio loop. The class still exposes async
methods to match BaseVectorStorage.
"""
from __future__ import annotations

import hashlib
import logging
import os
import uuid
from dataclasses import dataclass

import numpy as np
from qdrant_client import QdrantClient
from qdrant_client.http import models as qm
from tqdm import tqdm

from .._utils import logger
from ..base import BaseVectorStorage
from .._videoutil import encode_video_segments, encode_video_frames, encode_string_query

_qlog = logging.getLogger("videorag.qdrant")

_NAMESPACE = uuid.UUID("4b9a2c11-9f3e-4c2a-9c9d-2bf6c1e5d7a2")


def _scope_from_video_name(video_name: str) -> str:
    if "__" in video_name:
        return video_name.split("__", 1)[0]
    return hashlib.sha256(video_name.encode("utf-8")).hexdigest()[:16]


def _point_id(video_name: str, index: str) -> str:
    return str(uuid.uuid5(_NAMESPACE, f"{video_name}|{index}"))


def _frame_point_id(video_name: str, segment_index: str, frame_index: int) -> str:
    """Stable per-frame point id (5 frames per segment by default)."""
    return str(uuid.uuid5(_NAMESPACE, f"{video_name}|{segment_index}|f{frame_index}"))


def _qdrant_url() -> str:
    return os.environ.get("QDRANT_URL", "http://qdrant:6333")


def _qdrant_api_key() -> str | None:
    val = os.environ.get("QDRANT_API_KEY")
    return val or None


def _segment_collection() -> str:
    # Renamed default from `meai_rag_video_segments` to `meai_rag_video_frames` to
    # signal the schema change (one row per frame, not per segment). The old
    # collection — if it exists — is now orphaned and can be safely deleted.
    return os.environ.get("VIDEORAG_QDRANT_SEGMENT_COLLECTION", "meai_rag_video_frames")


def _segment_dim() -> int:
    return int(os.environ.get("VIDEORAG_SEGMENT_DIM", "3072"))


_COLLECTION_INITIALIZED: set[str] = set()


def _ensure_collection(client: QdrantClient, name: str, dim: int) -> None:
    if name in _COLLECTION_INITIALIZED:
        return
    if not client.collection_exists(name):
        client.create_collection(
            collection_name=name,
            vectors_config=qm.VectorParams(size=dim, distance=qm.Distance.COSINE),
        )
        _qlog.info("Created Qdrant collection '%s' (dim=%d)", name, dim)
        client.create_payload_index(
            collection_name=name,
            field_name="scope",
            field_schema=qm.PayloadSchemaType.KEYWORD,
        )
        client.create_payload_index(
            collection_name=name,
            field_name="video_name",
            field_schema=qm.PayloadSchemaType.KEYWORD,
        )
    else:
        info = client.get_collection(name)
        current_dim = info.config.params.vectors.size
        if current_dim != dim:
            raise RuntimeError(
                f"Qdrant collection '{name}' has dim {current_dim}, but VideoRAG "
                f"is configured for {dim}. Recreate the collection or change the model."
            )
    _COLLECTION_INITIALIZED.add(name)


@dataclass
class QdrantVideoSegmentStorage(BaseVectorStorage):
    """Drop-in replacement for NanoVectorDBVideoSegmentStorage.

    Signature on upsert preserves upstream's non-standard call:
        upsert(video_name, segment_index2name, video_output_format)
    """

    embedding_func = None
    segment_retrieval_top_k: float = 4

    def __post_init__(self):
        self._client = QdrantClient(url=_qdrant_url(), api_key=_qdrant_api_key())
        self._collection = _segment_collection()
        self._dim = self.global_config.get("video_embedding_dim") or _segment_dim()
        self._max_batch_size = self.global_config.get("video_embedding_batch_num", 2)
        self.top_k = self.global_config.get(
            "segment_retrieval_top_k", self.segment_retrieval_top_k
        )
        # Allow caller to scope queries (and tag upserts) by overriding the
        # scope hash that would otherwise be parsed from `<scope>__<post_id>`.
        self._scope_override: str | None = self.global_config.get("videorag_scope")
        _ensure_collection(self._client, self._collection, self._dim)

    async def upsert(self, video_name, segment_index2name, video_output_format):
        """Frame-level upsert. Each segment becomes N Qdrant points (default N=5,
        configurable via VIDEORAG_FRAMES_PER_SEGMENT). Each point carries the
        frame's S3 URL in its payload so downstream image-rerank can use the
        actual relevant frame as a visual reference for image-gen.
        """
        if not len(segment_index2name):
            logger.warning("Empty segment_index2name for %s", video_name)
            return []

        scope = self._scope_override or _scope_from_video_name(video_name)
        cache_path = os.path.join(self.global_config["working_dir"], "_cache", video_name)

        index_list = list(segment_index2name.keys())
        video_paths: list[str] = []
        seg_path_to_index: dict[str, str] = {}
        for index in index_list:
            segment_name = segment_index2name[index]
            seg_path = os.path.join(cache_path, f"{segment_name}.{video_output_format}")
            video_paths.append(seg_path)
            seg_path_to_index[seg_path] = str(index)

        # Batched encode: produces a flat list of frame records across all
        # segments. Each record has its own vector + S3 frame URL + segment_id.
        frame_records: list[dict] = []
        batches = [
            video_paths[i: i + self._max_batch_size]
            for i in range(0, len(video_paths), self._max_batch_size)
        ]
        for _batch in tqdm(batches, desc=f"Encoding video frames {video_name}"):
            try:
                frame_records.extend(encode_video_frames(_batch))
            except Exception as ex:
                logger.warning("encode_video_frames failed for batch: %s", ex)

        if not frame_records:
            logger.warning("No frame records produced for %s — nothing to upsert", video_name)
            return []

        # Map each frame's segment path back to the segment index used by the
        # parent VideoRAG instance, so the payload's `index` field stays the
        # same shape that the downstream `text leg` uses for joining.
        points: list[qm.PointStruct] = []
        for rec in frame_records:
            seg_path = rec.get("segment_path") or ""
            seg_index = seg_path_to_index.get(seg_path)
            if seg_index is None:
                # Fall back to filename-derived id if the path map missed.
                seg_index = rec.get("segment_id") or "unknown"
            frame_index = int(rec.get("frame_index") or 0)
            frame_url = rec.get("frame_url")
            if not frame_url:
                continue
            points.append(qm.PointStruct(
                id=_frame_point_id(video_name, seg_index, frame_index),
                vector=np.asarray(rec["vector"], dtype=np.float32).tolist(),
                payload={
                    "scope": scope,
                    "video_name": video_name,
                    # `index` retained for back-compat with the text leg's join key.
                    "index": str(seg_index),
                    "segment_name": segment_index2name.get(seg_index, str(seg_index)),
                    "frame_index": frame_index,
                    "frame_url": frame_url,
                },
            ))
        if points:
            self._client.upsert(collection_name=self._collection, points=points)
        logger.info(
            "Upserted %d frame vectors to Qdrant '%s' (scope=%s, segments=%d, frames/seg=%d)",
            len(points), self._collection, scope, len(index_list),
            (len(points) // max(len(index_list), 1)),
        )
        return [{"id": p.id, "scope": scope} for p in points]

    async def query(self, query: str):
        """Frame-level cosine retrieval, then collapsed to one hit per segment.
        Each surviving segment hit carries the URL of its highest-scoring frame
        — that's what downstream image-rerank uses as the visual reference candidate.

        We over-fetch (top_k * frames_per_segment) so that even if the matches
        cluster within a few segments we can still return up to `top_k` distinct
        segments. Without over-fetching, all 5 frames of one popular segment
        could fill the result before any other segment shows up.
        """
        embedding = encode_string_query(query).numpy()[0]
        scope = self._scope_override
        flt: qm.Filter | None = None
        if scope:
            flt = qm.Filter(
                must=[qm.FieldCondition(key="scope", match=qm.MatchValue(value=scope))]
            )
        # Frames-per-segment over-fetch: we want top_k DISTINCT segments back.
        frames_per_seg = int(os.environ.get("VIDEORAG_FRAMES_PER_SEGMENT", "5"))
        raw_limit = max(self.top_k * max(frames_per_seg, 1), int(self.top_k))
        result = self._client.query_points(
            collection_name=self._collection,
            query=embedding.astype(np.float32).tolist(),
            limit=raw_limit,
            query_filter=flt,
            with_payload=True,
        )

        # Group frame hits by (video_name, segment_index); take the best frame per
        # segment and remember its URL. Result is one hit per segment, score = the
        # max frame score within that segment.
        best_per_seg: dict[tuple[str, str], dict] = {}
        for p in result.points:
            payload = p.payload or {}
            video_name = payload.get("video_name", "")
            idx = str(payload.get("index", ""))
            score = float(p.score) if p.score is not None else 0.0
            frame_url = payload.get("frame_url")
            frame_idx = payload.get("frame_index")
            key = (video_name, idx)
            existing = best_per_seg.get(key)
            if existing is None or score > existing["distance"]:
                best_per_seg[key] = {
                    "id": f"{video_name}_{idx}",
                    "__id__": f"{video_name}_{idx}",
                    "__video_name__": video_name,
                    "__index__": idx,
                    "distance": score,
                    "scope": payload.get("scope"),
                    "frame_url": frame_url,
                    "frame_index": frame_idx,
                }

        # Sort by score, cap at top_k segments.
        hits = sorted(best_per_seg.values(), key=lambda h: -h["distance"])[: int(self.top_k)]
        return hits

    async def index_done_callback(self):
        # Qdrant persists synchronously; nothing to flush.
        return None
