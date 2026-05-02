"""Service entrypoint: boots the RAG engine + RabbitMQ consumer + health server.

Run with: python -m service.main
"""
from __future__ import annotations

import asyncio
import logging
import signal

import uvicorn

from .config import load_config
from .health import build_app
from .rabbit_consumer import RabbitConsumer
from .rag_engine import RagEngine


async def run() -> None:
    cfg = load_config()
    logging.basicConfig(
        level=cfg.log_level,
        format="%(asctime)s %(levelname)s %(name)s | %(message)s",
    )
    logger = logging.getLogger("rag-service")

    engine = RagEngine(cfg)
    await engine.initialize()

    consumer = RabbitConsumer(cfg, engine)
    await consumer.start()

    health_config = uvicorn.Config(
        build_app(),
        host="0.0.0.0",
        port=cfg.health_port,
        log_level=cfg.log_level.lower(),
        access_log=False,
    )
    health_server = uvicorn.Server(health_config)
    health_task = asyncio.create_task(health_server.serve())

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

    logger.info("Shutting down...")
    health_server.should_exit = True
    await health_task
    await consumer.stop()
    await engine.close()


if __name__ == "__main__":
    asyncio.run(run())
