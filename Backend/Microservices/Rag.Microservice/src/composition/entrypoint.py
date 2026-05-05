"""Service entrypoint. Boots config → container → transports → wait → shutdown.

Run:    python -m src.composition.entrypoint
"""
from __future__ import annotations

import asyncio
import logging
import signal

import uvicorn

from ..transport.grpc.rag_ingest_servicer import GrpcServer
from ..transport.health_app import build_app
from ..transport.rabbit_consumer import RabbitConsumer
from .config import load_config
from .container import build_container, close_async, initialize_async
from .seed_loader import (
    apply_filesystem_seed_if_needed,
    apply_qdrant_seed_if_needed,
)


async def run() -> None:
    cfg = load_config()
    logging.basicConfig(
        level=cfg.log_level,
        format="%(asctime)s %(levelname)s %(name)s | %(message)s",
    )
    logger = logging.getLogger("rag-service")

    # ── Seed restore phase A (filesystem) ────────────────────────────────
    # Must run BEFORE LightRAG initializes so it sees the populated workspace
    # state (kv_store_*.json + graphml + merged fingerprint registry). No-op
    # when no baked seed is present (dev compose with empty bakedknowledge/).
    seed_manifest = apply_filesystem_seed_if_needed(
        working_dir=cfg.working_dir,
        qdrant_namespace=cfg.qdrant_namespace,
    )

    container = build_container(cfg)
    await initialize_async(container)

    # ── Seed restore phase B (Qdrant points) ─────────────────────────────
    # Must run AFTER initialize_async so LightRAG has created the collections
    # with the right payload indexes. Idempotent same-id upsert.
    if seed_manifest is not None:
        await apply_qdrant_seed_if_needed(
            qdrant_url=cfg.qdrant_url,
            qdrant_api_key=cfg.qdrant_api_key,
            working_dir=cfg.working_dir,
            manifest=seed_manifest,
        )

    # ── Transport: RabbitMQ (ingest one-way + query RPC) ──────────────────
    rabbit = RabbitConsumer(
        rabbit_url=cfg.rabbit_url,
        ingest_queue=cfg.rabbit_ingest_queue,
        query_queue=cfg.rabbit_query_queue,
        prefetch=cfg.prefetch,
        ingest=container.ingest_service,
        query=container.query_service,
        lazy_bootstrap=container.lazy_knowledge_bootstrap,
    )
    await rabbit.start()

    # ── Transport: gRPC (synchronous batch ingest) ────────────────────────
    grpc_server = GrpcServer(
        container.ingest_service,
        container.lazy_knowledge_bootstrap,
        port=cfg.grpc_port,
    )
    await grpc_server.start()

    # ── Transport: HTTP health ────────────────────────────────────────────
    health_config = uvicorn.Config(
        build_app(),
        host="0.0.0.0",
        port=cfg.health_port,
        log_level=cfg.log_level.lower(),
        access_log=False,
    )
    health_server = uvicorn.Server(health_config)
    health_task = asyncio.create_task(health_server.serve())

    # Knowledge bootstrap is no longer fired on startup. The first incoming
    # request (RabbitMQ ingest/query OR gRPC IngestBatch) calls
    # `LazyKnowledgeBootstrap.trigger()` which schedules bootstrap as a
    # background task at that point. Idle services pay zero LLM cost.

    # ── Wait for stop signal ──────────────────────────────────────────────
    stop = asyncio.Event()
    loop = asyncio.get_running_loop()
    for sig in (signal.SIGINT, signal.SIGTERM):
        try:
            loop.add_signal_handler(sig, stop.set)
        except NotImplementedError:
            # Windows dev hosts: signal handlers may not be supported in the event loop.
            pass

    logger.info("RAG microservice running. Waiting for messages...")
    await stop.wait()

    # ── Shutdown ──────────────────────────────────────────────────────────
    logger.info("Shutting down...")
    # If bootstrap was triggered by some prior request and is still in flight,
    # cancel it so we don't wait minutes on shutdown.
    bootstrap_task = container.lazy_knowledge_bootstrap.task
    if bootstrap_task is not None and not bootstrap_task.done():
        bootstrap_task.cancel()
        try:
            await bootstrap_task
        except (asyncio.CancelledError, Exception):
            pass
    health_server.should_exit = True
    await health_task
    await grpc_server.stop()
    await rabbit.stop()
    await close_async(container)


if __name__ == "__main__":
    asyncio.run(run())
