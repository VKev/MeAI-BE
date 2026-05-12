"""Composition root — manual DI.

Builds the entire object graph at startup, in dependency order:
  config → infra adapters → services → transport handlers

Manual wiring is intentional. At ~12 services we don't need a DI framework;
the container reads top-to-bottom like a recipe and that's worth more than
any auto-wiring magic. Each new dependency adds one line here, nothing else.
"""
from __future__ import annotations

import logging
import os
from dataclasses import dataclass
from pathlib import Path

from ..application.services.ingest_service import IngestService
from ..application.services.knowledge_bootstrap_service import KnowledgeBootstrapService
from ..application.services.lazy_knowledge_bootstrap import LazyKnowledgeBootstrap
from ..application.services.query_service import QueryService
from ..infrastructure.fingerprint_registry import JsonFileFingerprintRegistry
from ..infrastructure.knowledge_loader import FilesystemKnowledgeLoader
from ..infrastructure.lightrag_facade import LightRagFacadeImpl
from ..infrastructure.multimodal_embedder import OpenRouterMultimodalEmbedder
from ..infrastructure.qdrant_visual_store import QdrantVisualStore
from ..infrastructure.s3_image_mirror import S3ImageMirror
from ..infrastructure.video_rag import VideoRagAdapter
from ..infrastructure.vision_describer import OpenAIVisionDescriber
from .config import Config

logger = logging.getLogger("rag-service.container")


@dataclass
class Container:
    """All wired components. Lifecycle: build → initialize_async → use → close."""

    cfg: Config

    # Infrastructure adapters
    light_rag: LightRagFacadeImpl
    vision_describer: OpenAIVisionDescriber
    multimodal_embedder: OpenRouterMultimodalEmbedder | None
    visual_store: QdrantVisualStore | None
    image_mirror: S3ImageMirror | None
    video_rag: VideoRagAdapter | None
    fingerprints: JsonFileFingerprintRegistry
    knowledge_loader: FilesystemKnowledgeLoader

    # Services
    ingest_service: IngestService
    query_service: QueryService
    knowledge_bootstrap: KnowledgeBootstrapService
    lazy_knowledge_bootstrap: LazyKnowledgeBootstrap


def _no_presign(_: str | None) -> str | None:
    """Fallback presigner when S3 isn't configured — visual store hits will
    return None for `mirroredImageUrl` (caller falls back to raw imageUrl)."""
    return None


def build_container(cfg: Config) -> Container:
    """Build the full object graph synchronously. Run `initialize_async` next
    to open async resources (LightRAG storages, Qdrant collection, VideoRAG
    workdirs)."""

    os.makedirs(cfg.working_dir, exist_ok=True)

    # ── Infrastructure ────────────────────────────────────────────────────

    fingerprints = JsonFileFingerprintRegistry(
        Path(cfg.working_dir) / "ingested_ids.json"
    )
    fingerprints.load_sync()

    light_rag = LightRagFacadeImpl(
        working_dir=cfg.working_dir,
        log_level=cfg.log_level,
        llm_base_url=cfg.llm_base_url,
        llm_api_key=cfg.llm_api_key,
        llm_model=cfg.llm_model,
        embed_base_url=cfg.embed_base_url,
        embed_api_key=cfg.embed_api_key,
        embed_model=cfg.embed_model,
        embed_dim=cfg.embed_dim,
        embed_max_tokens=cfg.embed_max_tokens,
        qdrant_url=cfg.qdrant_url,
        qdrant_api_key=cfg.qdrant_api_key,
        qdrant_namespace=cfg.qdrant_namespace,
        rerank_api_key=cfg.rerank_api_key,
        rerank_base_url=cfg.rerank_base_url,
        rerank_model=cfg.rerank_model,
    )

    vision_describer = OpenAIVisionDescriber(
        base_url=cfg.llm_base_url,
        api_key=cfg.llm_api_key,
        model=cfg.llm_model,
    )

    multimodal_embedder: OpenRouterMultimodalEmbedder | None = OpenRouterMultimodalEmbedder(
        base_url=cfg.multimodal_embed_base_url,
        api_key=cfg.multimodal_embed_api_key,
        model=cfg.multimodal_embed_model,
        max_concurrency=cfg.multimodal_embed_max_concurrency,
    )

    image_mirror: S3ImageMirror | None = None
    if cfg.s3_bucket:
        image_mirror = S3ImageMirror(
            bucket=cfg.s3_bucket,
            region=cfg.s3_region,
            key_prefix=cfg.s3_image_key_prefix,
            ttl_seconds=cfg.s3_presign_ttl_seconds,
            public_base_url=cfg.s3_public_base_url,
        )

    presigner = image_mirror.presign if image_mirror else _no_presign
    visual_store: QdrantVisualStore | None = QdrantVisualStore(
        url=cfg.qdrant_url,
        api_key=cfg.qdrant_api_key,
        collection=cfg.multimodal_visual_collection,
        presigner=presigner,
    )

    video_rag: VideoRagAdapter | None = None
    if cfg.videorag_enabled:
        video_rag = VideoRagAdapter(
            working_dir=cfg.working_dir,
            videorag_workdir_root=cfg.videorag_workdir_root,
            instance_cache_max=cfg.videorag_instance_cache_max,
            segment_length_seconds=cfg.videorag_segment_length_seconds,
            frames_per_segment=cfg.videorag_frames_per_segment,
            segment_dim=cfg.videorag_segment_dim,
            llm_base_url=cfg.llm_base_url,
            llm_api_key=cfg.llm_api_key,
            multimodal_embed_base_url=cfg.multimodal_embed_base_url,
            multimodal_embed_api_key=cfg.multimodal_embed_api_key,
            multimodal_embed_model=cfg.multimodal_embed_model,
        )

    knowledge_loader = FilesystemKnowledgeLoader(cfg.knowledge_dir)

    # ── Services ──────────────────────────────────────────────────────────

    # If no S3 bucket is configured we still need *some* image mirror impl
    # for IngestService's image_native path. Build a no-op fallback.
    if image_mirror is None:
        # Tiny inline shim — keeps the Protocol satisfied without making
        # IngestService's ImageMirrorPort dep nullable (which would leak
        # the optionality into the service's branching logic).
        class _NoOpMirror:
            async def upload(self, *_, **__) -> str | None:
                return None
            def presign(self, _: str | None) -> str | None:
                return None
        image_mirror = _NoOpMirror()  # type: ignore[assignment]

    ingest_service = IngestService(
        fingerprints=fingerprints,
        light_rag=light_rag,
        vision_describer=vision_describer,
        multimodal_embedder=multimodal_embedder,
        visual_store=visual_store,
        image_mirror=image_mirror,  # type: ignore[arg-type]
        video_rag=video_rag,
    )

    query_service = QueryService(
        fingerprints=fingerprints,
        light_rag=light_rag,
        multimodal_embedder=multimodal_embedder,
        visual_store=visual_store,
        video_rag=video_rag,
    )

    knowledge_bootstrap = KnowledgeBootstrapService(
        loader=knowledge_loader,
        ingest=ingest_service,
    )
    lazy_knowledge_bootstrap = LazyKnowledgeBootstrap(knowledge_bootstrap)

    return Container(
        cfg=cfg,
        light_rag=light_rag,
        vision_describer=vision_describer,
        multimodal_embedder=multimodal_embedder,
        visual_store=visual_store,
        image_mirror=image_mirror,  # type: ignore[arg-type]
        video_rag=video_rag,
        fingerprints=fingerprints,
        knowledge_loader=knowledge_loader,
        ingest_service=ingest_service,
        query_service=query_service,
        knowledge_bootstrap=knowledge_bootstrap,
        lazy_knowledge_bootstrap=lazy_knowledge_bootstrap,
    )


async def initialize_async(c: Container) -> None:
    """Open async resources. Order matters: LightRAG → visual store → VideoRAG.

    Knowledge bootstrap is NOT run here, and is no longer fired on startup at
    all. The transport layer triggers it lazily on the FIRST incoming request
    via `LazyKnowledgeBootstrap.trigger()` — that way the service boots fast
    even when the volume is empty, and bootstrap only pays its LLM cost when
    the service is actually being used.
    """
    await c.light_rag.initialize_async()

    if c.visual_store is not None:
        await c.visual_store.initialize(c.cfg.multimodal_embed_dim)
        logger.info(
            "Multimodal path ready (model=%s dim=%d collection=%s)",
            c.cfg.multimodal_embed_model,
            c.cfg.multimodal_embed_dim,
            c.cfg.multimodal_visual_collection,
        )

    if c.video_rag is not None:
        await c.video_rag.initialize()
        logger.info(
            "VideoRAG path ready (collection=%s dim=%d frames/seg=%d cache_max=%d)",
            c.cfg.videorag_segment_collection,
            c.cfg.videorag_segment_dim,
            c.cfg.videorag_frames_per_segment,
            c.cfg.videorag_instance_cache_max,
        )
    else:
        logger.info("VideoRAG disabled by VIDEORAG_ENABLED")


# Note: the eager `run_knowledge_bootstrap(...)` helper was removed. Bootstrap
# now runs lazily — see `LazyKnowledgeBootstrap` and the transport handlers'
# `self._lazy_bootstrap.trigger()` calls.


async def close_async(c: Container) -> None:
    """Lifecycle teardown — best-effort, never raises."""
    try:
        await c.light_rag.close()
    except Exception:
        logger.exception("Error closing LightRAG facade")
    if c.visual_store is not None:
        try:
            await c.visual_store.close()
        except Exception:
            logger.exception("Error closing visual store")
    if c.video_rag is not None:
        try:
            await c.video_rag.close()
        except Exception:
            logger.exception("Error closing video RAG adapter")
