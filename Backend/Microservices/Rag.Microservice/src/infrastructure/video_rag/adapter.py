"""VideoRagAdapter — implements `VideoRagEnginePort`.

Wraps the vendored `videorag/` library at the repo root. Per-account isolation
via per-scope working directories + LRU cache of `VideoRAG` instances.

Logic preserved from `service/videorag_engine.py`; constructor now takes
explicit parameters instead of a Config object so the dependency is honest.
"""
from __future__ import annotations

import asyncio
import hashlib
import logging
import os
import shutil
from collections import OrderedDict
from typing import Any

import numpy as np

from .downloader import fetch_video_to_temp

logger = logging.getLogger("rag-service.videorag-engine")


def _scope_hash(platform: str, social_media_id: str) -> str:
    raw = f"{platform.lower()}|{social_media_id}".encode("utf-8")
    return hashlib.sha256(raw).hexdigest()[:16]


class VideoRagAdapter:
    """Implements `VideoRagEnginePort`. Top-level wrapper around a fleet of
    per-scope VideoRAG instances."""

    def __init__(
        self,
        *,
        working_dir: str,
        videorag_workdir_root: str,
        instance_cache_max: int,
        segment_length_seconds: int,
        frames_per_segment: int,
        segment_dim: int,
        # env-mirroring for spawned multiprocessing workers (videorag's caption /
        # feature encoders re-import openrouter_helpers and read env at import)
        llm_base_url: str,
        llm_api_key: str,
        multimodal_embed_base_url: str,
        multimodal_embed_api_key: str,
        multimodal_embed_model: str,
    ) -> None:
        self._cache: "OrderedDict[str, Any]" = OrderedDict()
        self._cache_lock = asyncio.Lock()
        self._max_instances = instance_cache_max
        self._workdir_root = videorag_workdir_root
        os.makedirs(self._workdir_root, exist_ok=True)

        # Cached config for spawned workers + per-instance build.
        self._segment_length = segment_length_seconds
        self._frames_per_segment = frames_per_segment
        self._segment_dim = segment_dim

        # Env mirror — child processes inherit these.
        os.environ.setdefault("LLM_BASE_URL", llm_base_url)
        os.environ.setdefault("LLM_API_KEY", llm_api_key)
        os.environ.setdefault("MULTIMODAL_EMBED_BASE_URL", multimodal_embed_base_url)
        os.environ.setdefault("MULTIMODAL_EMBED_API_KEY", multimodal_embed_api_key)
        os.environ.setdefault("MULTIMODAL_EMBED_MODEL", multimodal_embed_model)
        # Upstream VideoRAG uses AsyncOpenAI() with no args — feed it OpenRouter creds
        os.environ.setdefault("OPENAI_API_KEY", llm_api_key)
        os.environ.setdefault("OPENAI_BASE_URL", llm_base_url)

    async def initialize(self) -> None:
        logger.info(
            "VideoRAG engine ready (workdir_root=%s lru_max=%d)",
            self._workdir_root, self._max_instances,
        )

    def _scope_workdir(self, scope: str) -> str:
        return os.path.join(self._workdir_root, scope)

    async def _evict_if_needed(self) -> None:
        while len(self._cache) > self._max_instances:
            evict_scope, evict_inst = self._cache.popitem(last=False)
            try:
                await evict_inst._save_video_segments()
            except Exception:
                logger.warning("Eviction flush failed for %s", evict_scope, exc_info=True)
            logger.info("Evicted VideoRAG instance for scope=%s", evict_scope)

    async def _get_instance(self, scope: str):
        from ._lib import VideoRAG
        from ._lib._storage.vdb_qdrant import QdrantVideoSegmentStorage
        from ._lib._llm import openai_config

        async with self._cache_lock:
            if scope in self._cache:
                inst = self._cache.pop(scope)
                self._cache[scope] = inst
                return inst

            workdir = self._scope_workdir(scope)
            os.makedirs(workdir, exist_ok=True)

            inst = VideoRAG(
                working_dir=workdir,
                llm=openai_config,
                vs_vector_db_storage_cls=QdrantVideoSegmentStorage,
                vector_db_storage_cls_kwargs={"videorag_scope": scope},
                always_create_working_dir=True,
                video_segment_length=self._segment_length,
                rough_num_frames_per_segment=self._frames_per_segment,
                video_embedding_dim=self._segment_dim,
            )
            inst.video_segment_feature_vdb._scope_override = scope
            inst.load_caption_model(debug=True)

            self._cache[scope] = inst
            await self._evict_if_needed()
            return inst

    async def ingest_video(self, payload: dict[str, Any]) -> dict[str, Any]:
        platform = payload.get("platform") or ""
        social_media_id = payload.get("socialMediaId") or payload.get("social_media_id") or ""
        post_id = payload.get("postId") or payload.get("post_id") or ""
        video_url = payload.get("videoUrl") or payload.get("video_url")
        if not (platform and social_media_id and post_id and video_url):
            raise ValueError("ingest_video requires platform, socialMediaId, postId, videoUrl")

        scope = _scope_hash(platform, social_media_id)
        inst = await self._get_instance(scope)

        loop = asyncio.get_running_loop()

        def _do_insert() -> dict[str, Any]:
            with fetch_video_to_temp(
                video_url, scope_hash=scope, post_id=post_id, ext="mp4",
            ) as path:
                inst.insert_video(video_path_list=[path])
            return {
                "status": "ingested",
                "scope": scope,
                "platform": platform,
                "socialMediaId": social_media_id,
                "postId": post_id,
            }

        return await loop.run_in_executor(None, _do_insert)

    async def query_video(
        self,
        query: str,
        *,
        platform: str,
        social_media_id: str,
        top_k: int = 4,
    ) -> list[dict[str, Any]]:
        scope = _scope_hash(platform, social_media_id)
        inst = await self._get_instance(scope)

        visual_task = self._query_visual_leg(inst, query, scope, top_k)
        text_task = self._query_text_leg(inst, query, top_k)
        visual_hits, text_hits = await asyncio.gather(
            visual_task, text_task, return_exceptions=True,
        )
        if isinstance(visual_hits, Exception):
            logger.warning("video query visual leg failed: %s", visual_hits)
            visual_hits = []
        if isinstance(text_hits, Exception):
            logger.warning("video query text leg failed: %s", text_hits)
            text_hits = []

        return self._rrf_fuse(visual_hits, text_hits, top_k=top_k)

    @staticmethod
    async def _query_visual_leg(inst, query: str, scope: str, top_k: int) -> list[dict[str, Any]]:
        inst.video_segment_feature_vdb.top_k = top_k
        inst.video_segment_feature_vdb._scope_override = scope
        return await inst.video_segment_feature_vdb.query(query)

    @staticmethod
    async def _query_text_leg(inst, query: str, top_k: int) -> list[dict[str, Any]]:
        if inst.chunks_vdb is None:
            return []
        text_chunks_kv = inst.text_chunks._data if inst.text_chunks is not None else {}
        if not text_chunks_kv:
            return []

        loop = asyncio.get_running_loop()

        def _embed_and_search() -> list[dict[str, Any]]:
            from ._lib.openrouter_helpers import openai_text_embedding
            try:
                emb = openai_text_embedding(query)
            except Exception as ex:
                logger.warning("video text-leg embed failed: %s", ex)
                return []
            try:
                return inst.chunks_vdb._client.query(
                    query=np.asarray(emb, dtype=np.float32),
                    top_k=max(top_k * 2, 4),
                    better_than_threshold=-1,
                )
            except Exception as ex:
                logger.warning("video text-leg nano-vectordb query failed: %s", ex)
                return []

        chunk_results = await loop.run_in_executor(None, _embed_and_search)

        seg_hits: list[dict[str, Any]] = []
        seen: set[str] = set()
        for r in chunk_results:
            chunk_id = r.get("__id__")
            chunk_data = text_chunks_kv.get(chunk_id, {})
            raw_seg = chunk_data.get("video_segment_id")
            # nano-graphrag may store either a single segment id (str) or a
            # list of segment ids per chunk depending on how the splitter ran.
            # Normalize to a list before iterating — passing a list to `set.add`
            # raises "unhashable type: 'list'" which previously killed the leg.
            if isinstance(raw_seg, list):
                seg_ids = [s for s in raw_seg if isinstance(s, str)]
            elif isinstance(raw_seg, str):
                seg_ids = [raw_seg]
            else:
                continue
            for seg_id in seg_ids:
                if not seg_id or seg_id in seen:
                    continue
                if "_" not in seg_id:
                    continue
                video_name, idx = seg_id.rsplit("_", 1)
                seen.add(seg_id)
                seg_hits.append({
                    "id": seg_id,
                    "__id__": seg_id,
                    "__video_name__": video_name,
                    "__index__": idx,
                    "distance": float(r.get("__metrics__", 0.0)),
                })
        return seg_hits[:top_k]

    @staticmethod
    def _rrf_fuse(
        visual_hits: list[dict[str, Any]],
        text_hits: list[dict[str, Any]],
        *,
        top_k: int,
        k: int = 60,
    ) -> list[dict[str, Any]]:
        rrf: dict[str, float] = {}
        by_key: dict[str, dict[str, Any]] = {}
        sources: dict[str, set[str]] = {}

        def _add(hits: list[dict[str, Any]], label: str) -> None:
            for i, hit in enumerate(hits):
                key = hit.get("id") or hit.get("__id__")
                if not key:
                    continue
                rrf[key] = rrf.get(key, 0.0) + 1.0 / (k + i + 1)
                sources.setdefault(key, set()).add(label)
                if key not in by_key:
                    by_key[key] = dict(hit)

        _add(visual_hits, "visual")
        _add(text_hits, "text")

        fused: list[dict[str, Any]] = []
        for key, hit in by_key.items():
            merged = dict(hit)
            merged["rrf_score"] = rrf[key]
            merged["distance"] = rrf[key]
            merged["source"] = "+".join(sorted(sources[key]))
            fused.append(merged)

        fused.sort(key=lambda h: h["rrf_score"], reverse=True)
        return fused[:top_k]

    async def hydrate_segments(
        self, scope: str, hits: list[dict[str, Any]]
    ) -> list[dict[str, Any]]:
        if not hits:
            return []
        try:
            inst = await self._get_instance(scope)
        except Exception:
            logger.warning("hydrate_segments: no instance for scope=%s", scope)
            return hits
        out: list[dict[str, Any]] = []
        for h in hits:
            video_name = h.get("__video_name__") or h.get("videoName") or ""
            index = h.get("__index__") or h.get("index") or ""
            seg = None
            try:
                video_segments = inst.video_segments._data.get(video_name)
                if isinstance(video_segments, dict):
                    seg = video_segments.get(str(index))
            except Exception:
                seg = None
            entry = dict(h)
            if seg:
                entry["caption"] = seg.get("content")
                entry["transcript"] = seg.get("transcript")
                entry["time"] = seg.get("time")
            entry["videoName"] = video_name
            entry["postId"] = video_name.split("__", 1)[1] if "__" in video_name else video_name
            out.append(entry)
        return out

    async def close(self) -> None:
        async with self._cache_lock:
            for scope, inst in list(self._cache.items()):
                try:
                    await inst._save_video_segments()
                except Exception:
                    logger.warning("close: flush failed for %s", scope, exc_info=True)
            self._cache.clear()

    @staticmethod
    def scope_for(platform: str, social_media_id: str) -> str:
        return _scope_hash(platform, social_media_id)

    def remove_workdir(self, scope: str) -> None:
        path = self._scope_workdir(scope)
        if os.path.isdir(path):
            shutil.rmtree(path, ignore_errors=True)
