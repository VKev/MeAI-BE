"""Env-driven configuration for the RAG microservice wrapper."""
from __future__ import annotations

import os
from dataclasses import dataclass


@dataclass
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

    qdrant_url: str
    qdrant_api_key: str | None
    qdrant_namespace: str

    working_dir: str
    log_level: str
    health_port: int


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
        qdrant_url=os.environ.get("QDRANT_URL", "http://qdrant:6333"),
        qdrant_api_key=os.environ.get("QDRANT_API_KEY") or None,
        qdrant_namespace=os.environ.get("QDRANT_NAMESPACE", "meai_rag"),
        working_dir=os.environ.get("WORKING_DIR", "/data/rag_storage"),
        log_level=os.environ.get("LOG_LEVEL", "INFO"),
        health_port=int(os.environ.get("HEALTH_PORT", "8000")),
    )
