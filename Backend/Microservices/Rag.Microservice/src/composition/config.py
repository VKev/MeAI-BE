"""Env-driven configuration. Single source of truth for runtime settings.

Env-var names preserved verbatim from the pre-refactor `service/config.py`
so existing docker-compose env blocks keep working.
"""
from __future__ import annotations

import os
from dataclasses import dataclass


@dataclass(slots=True)
class Config:
    rabbit_url: str
    rabbit_ingest_queue: str
    rabbit_query_queue: str
    prefetch: int

    llm_base_url: str
    llm_api_key: str
    llm_model: str

    embed_base_url: str
    embed_api_key: str
    embed_model: str
    embed_dim: int
    embed_max_tokens: int

    multimodal_embed_base_url: str
    multimodal_embed_api_key: str
    multimodal_embed_model: str
    multimodal_embed_dim: int
    multimodal_embed_max_concurrency: int
    multimodal_visual_collection: str

    # Jina rerank — wired into LightRAG's text-mode retrieval as
    # `rerank_model_func`. Empty `rerank_api_key` disables (silences the
    # LightRAG "Rerank is enabled but no rerank model is configured" warning).
    rerank_api_key: str
    rerank_base_url: str
    rerank_model: str

    qdrant_url: str
    qdrant_api_key: str | None
    qdrant_namespace: str

    videorag_enabled: bool
    videorag_workdir_root: str
    videorag_segment_collection: str
    videorag_segment_dim: int
    videorag_segment_top_k: int
    videorag_instance_cache_max: int
    videorag_llm_model: str
    videorag_audio_model: str
    videorag_frames_per_segment: int
    videorag_segment_length_seconds: int
    videorag_max_duration_seconds: int

    s3_bucket: str | None
    s3_region: str
    s3_image_key_prefix: str
    s3_presign_ttl_seconds: int

    knowledge_dir: str
    working_dir: str
    log_level: str
    health_port: int
    grpc_port: int


def _required(name: str) -> str:
    value = os.environ.get(name)
    if not value:
        raise RuntimeError(f"Required env var {name} is not set")
    return value


def load_config() -> Config:
    rabbit_user = os.environ.get("RABBITMQ_USER", "rabbitmq")
    rabbit_pass = os.environ.get("RABBITMQ_PASS", "rabbitmq")
    rabbit_host = os.environ.get("RABBITMQ_HOST", "rabbit-mq")
    rabbit_port = os.environ.get("RABBITMQ_PORT", "5672")
    rabbit_url = (
        os.environ.get("RABBITMQ_URL")
        or f"amqp://{rabbit_user}:{rabbit_pass}@{rabbit_host}:{rabbit_port}/"
    )

    llm_base_url = _required("LLM_BASE_URL")
    llm_api_key = _required("LLM_API_KEY")

    working_dir = os.environ.get("WORKING_DIR", "/data/rag_storage")

    return Config(
        rabbit_url=rabbit_url,
        rabbit_ingest_queue=os.environ.get("RABBIT_INGEST_QUEUE", "meai.rag.ingest"),
        rabbit_query_queue=os.environ.get("RABBIT_QUERY_QUEUE", "meai.rag.query"),
        prefetch=int(os.environ.get("RABBIT_PREFETCH", "4")),
        llm_base_url=llm_base_url,
        llm_api_key=llm_api_key,
        llm_model=os.environ.get("LLM_MODEL", "openai/gpt-4o-mini"),
        embed_base_url=os.environ.get("EMBED_BASE_URL", llm_base_url),
        embed_api_key=os.environ.get("EMBED_API_KEY", llm_api_key),
        embed_model=os.environ.get("EMBED_MODEL", "openai/text-embedding-3-small"),
        embed_dim=int(os.environ.get("EMBED_DIM", "1536")),
        embed_max_tokens=int(os.environ.get("EMBED_MAX_TOKENS", "8192")),
        multimodal_embed_base_url=os.environ.get("MULTIMODAL_EMBED_BASE_URL", llm_base_url),
        multimodal_embed_api_key=os.environ.get("MULTIMODAL_EMBED_API_KEY", llm_api_key),
        multimodal_embed_model=os.environ.get(
            "MULTIMODAL_EMBED_MODEL", "nvidia/llama-nemotron-embed-vl-1b-v2:free"
        ),
        multimodal_embed_dim=int(os.environ.get("MULTIMODAL_EMBED_DIM", "2048")),
        multimodal_embed_max_concurrency=int(
            os.environ.get("MULTIMODAL_EMBED_MAX_CONCURRENCY", "2")
        ),
        multimodal_visual_collection=os.environ.get(
            "MULTIMODAL_VISUAL_COLLECTION", "meai_rag_visual"
        ),
        rerank_api_key=os.environ.get("RERANK_API_KEY", ""),
        rerank_base_url=os.environ.get(
            "RERANK_BASE_URL", "https://api.jina.ai/v1/rerank"
        ),
        rerank_model=os.environ.get("RERANK_MODEL", "jina-reranker-v2-base-multilingual"),
        qdrant_url=os.environ.get("QDRANT_URL", "http://qdrant:6333"),
        qdrant_api_key=os.environ.get("QDRANT_API_KEY") or None,
        qdrant_namespace=os.environ.get("QDRANT_NAMESPACE", "meai_rag"),
        videorag_enabled=os.environ.get("VIDEORAG_ENABLED", "1") not in ("0", "false", "False"),
        videorag_workdir_root=os.environ.get(
            "VIDEORAG_WORKDIR_ROOT",
            os.path.join(working_dir, "videorag"),
        ),
        videorag_segment_collection=os.environ.get(
            "VIDEORAG_QDRANT_SEGMENT_COLLECTION", "meai_rag_video_frames"
        ),
        videorag_segment_dim=int(os.environ.get("VIDEORAG_SEGMENT_DIM", "3072")),
        videorag_segment_top_k=int(os.environ.get("VIDEORAG_SEGMENT_TOP_K", "4")),
        videorag_instance_cache_max=int(os.environ.get("VIDEORAG_INSTANCE_CACHE_MAX", "8")),
        videorag_llm_model=os.environ.get("VIDEORAG_LLM_MODEL", "openai/gpt-4o-mini"),
        videorag_audio_model=os.environ.get("VIDEORAG_AUDIO_MODEL", "openai/whisper-1"),
        videorag_frames_per_segment=int(os.environ.get("VIDEORAG_FRAMES_PER_SEGMENT", "5")),
        videorag_segment_length_seconds=int(
            os.environ.get("VIDEORAG_SEGMENT_LENGTH_SECONDS", "5")
        ),
        videorag_max_duration_seconds=int(
            os.environ.get("VIDEORAG_MAX_DURATION_SECONDS", "600")
        ),
        s3_bucket=os.environ.get("VIDEORAG_S3_BUCKET") or None,
        s3_region=os.environ.get("VIDEORAG_S3_REGION", "ap-southeast-1"),
        s3_image_key_prefix=os.environ.get(
            "VIDEORAG_S3_IMAGE_KEY_PREFIX",
            os.environ.get("VIDEORAG_S3_KEY_PREFIX", "local-vinhdo/videorag-frames").rstrip("/")
            + "/images/",
        ),
        s3_presign_ttl_seconds=int(os.environ.get("VIDEORAG_S3_FRAME_TTL_SECONDS", "604800")),
        knowledge_dir=os.environ.get(
            "KNOWLEDGE_DIR", "/app/src/knowledge",
        ),
        working_dir=working_dir,
        log_level=os.environ.get("LOG_LEVEL", "INFO"),
        health_port=int(os.environ.get("HEALTH_PORT", "8000")),
        grpc_port=int(os.environ.get("GRPC_PORT", "5006")),
    )
