"""RabbitMQ consumer that exposes the RAG engine to other microservices.

Two queues:
  - <ingest queue> (one-way): consumes ingest commands. No reply.
  - <query queue> (RPC): consumes query commands. If the message has a
    `reply_to` and `correlation_id`, the result is published back to that
    routing key with the same correlation_id (standard AMQP RPC pattern).

Message body is JSON in both directions.
"""
from __future__ import annotations

import asyncio
import json
import logging
from typing import Any

import aio_pika
from aio_pika.abc import AbstractIncomingMessage

from .config import Config
from .rag_engine import RagEngine

logger = logging.getLogger("rag-service.rabbit")


class RabbitConsumer:
    def __init__(self, cfg: Config, engine: RagEngine) -> None:
        self.cfg = cfg
        self.engine = engine
        self.connection: aio_pika.RobustConnection | None = None
        self.channel: aio_pika.abc.AbstractRobustChannel | None = None

    async def start(self) -> None:
        self.connection = await aio_pika.connect_robust(self.cfg.rabbit_url)
        self.channel = await self.connection.channel()
        await self.channel.set_qos(prefetch_count=self.cfg.prefetch)

        ingest_queue = await self.channel.declare_queue(
            self.cfg.rabbit_ingest_queue, durable=True
        )
        await ingest_queue.consume(self._handle_ingest)

        query_queue = await self.channel.declare_queue(
            self.cfg.rabbit_query_queue, durable=True
        )
        await query_queue.consume(self._handle_query)

        logger.info(
            "Consuming queues: ingest=%s query=%s prefetch=%d",
            self.cfg.rabbit_ingest_queue,
            self.cfg.rabbit_query_queue,
            self.cfg.prefetch,
        )

    async def _handle_ingest(self, message: AbstractIncomingMessage) -> None:
        async with message.process(requeue=False, ignore_processed=True):
            payload = self._parse_body(message)
            if payload is None:
                return
            try:
                result = await self.engine.ingest(payload)
                logger.info("ingest done: %s", result)
            except Exception:
                logger.exception("ingest failed for payload: %s", payload)

    async def _handle_query(self, message: AbstractIncomingMessage) -> None:
        async with message.process(requeue=False, ignore_processed=True):
            payload = self._parse_body(message)
            if payload is None:
                await self._maybe_reply(message, {"error": "invalid_json_payload"})
                return
            try:
                op = (payload.get("op") or "query").lower()
                if op == "list_fingerprints":
                    result = await self.engine.list_fingerprints(payload)
                elif op == "multimodal_query":
                    result = await self.engine.multimodal_query(payload)
                else:
                    result = await self.engine.query(payload)
            except Exception as ex:
                logger.exception("query failed for payload: %s", payload)
                result = {"error": str(ex)}
            await self._maybe_reply(message, result)

    @staticmethod
    def _parse_body(message: AbstractIncomingMessage) -> dict | None:
        try:
            return json.loads(message.body.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError):
            logger.exception("Could not decode message body as JSON")
            return None

    async def _maybe_reply(self, message: AbstractIncomingMessage, body: Any) -> None:
        if not message.reply_to or self.channel is None:
            return
        await self.channel.default_exchange.publish(
            aio_pika.Message(
                body=json.dumps(body).encode("utf-8"),
                correlation_id=message.correlation_id,
                content_type="application/json",
            ),
            routing_key=message.reply_to,
        )

    async def stop(self) -> None:
        if self.connection is not None:
            await self.connection.close()
