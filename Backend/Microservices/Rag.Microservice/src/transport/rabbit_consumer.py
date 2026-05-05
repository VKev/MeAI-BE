"""RabbitMQ consumer — exposes ingest + query (RPC) over AMQP.

Two queues:
  - **ingest queue** (one-way): consumes ingest commands. No reply.
  - **query queue** (RPC): consumes query commands. If the message has a
    `reply_to` and `correlation_id`, the result is published back with the
    same correlation_id (standard AMQP RPC pattern).

Bodies are JSON. Ingest delegates to `IngestService`; query delegates to
`QueryService` based on the `op` field ("query" / "multimodal_query" /
"list_fingerprints").
"""
from __future__ import annotations

import asyncio
import json
import logging
from typing import Any

import aio_pika
from aio_pika.abc import AbstractIncomingMessage

from ..application.services.ingest_service import IngestService
from ..application.services.lazy_knowledge_bootstrap import LazyKnowledgeBootstrap
from ..application.services.query_service import QueryService

logger = logging.getLogger("rag-service.rabbit")


class RabbitConsumer:
    def __init__(
        self,
        *,
        rabbit_url: str,
        ingest_queue: str,
        query_queue: str,
        prefetch: int,
        ingest: IngestService,
        query: QueryService,
        lazy_bootstrap: LazyKnowledgeBootstrap,
    ) -> None:
        self._url = rabbit_url
        self._ingest_queue = ingest_queue
        self._query_queue = query_queue
        self._prefetch = prefetch
        self._ingest = ingest
        self._query = query
        self._lazy_bootstrap = lazy_bootstrap
        self._connection: aio_pika.RobustConnection | None = None
        self._channel: aio_pika.abc.AbstractRobustChannel | None = None

    async def start(self) -> None:
        self._connection = await aio_pika.connect_robust(self._url)
        self._channel = await self._connection.channel()
        await self._channel.set_qos(prefetch_count=self._prefetch)

        ingest_q = await self._channel.declare_queue(self._ingest_queue, durable=True)
        await ingest_q.consume(self._handle_ingest)

        query_q = await self._channel.declare_queue(self._query_queue, durable=True)
        await query_q.consume(self._handle_query)

        logger.info(
            "Consuming queues: ingest=%s query=%s prefetch=%d",
            self._ingest_queue, self._query_queue, self._prefetch,
        )

    async def _handle_ingest(self, message: AbstractIncomingMessage) -> None:
        # Block until knowledge bootstrap is ready. First caller does the work;
        # subsequent callers await the same already-completed task → instant.
        await self._lazy_bootstrap.ensure_ready()
        async with message.process(requeue=False, ignore_processed=True):
            payload = self._parse_body(message)
            if payload is None:
                return
            try:
                result = await self._ingest.ingest_one(payload)
                logger.info("ingest done: %s", result)
            except Exception:
                logger.exception("ingest failed for payload: %s", payload)

    async def _handle_query(self, message: AbstractIncomingMessage) -> None:
        await self._lazy_bootstrap.ensure_ready()
        async with message.process(requeue=False, ignore_processed=True):
            payload = self._parse_body(message)
            if payload is None:
                await self._maybe_reply(message, {"error": "invalid_json_payload"})
                return
            try:
                op = (payload.get("op") or "query").lower()
                if op == "wait_ready":
                    # ensure_ready() above already awaited the bootstrap. Reaching this
                    # point means the index is fully built — just confirm to the caller.
                    result = {"ready": True}
                elif op == "list_fingerprints":
                    result = await self._query.list_fingerprints(payload)
                elif op == "multimodal_query":
                    result = await self._query.multimodal_query(payload)
                else:
                    result = await self._query.text_query(payload)
            except Exception as ex:
                logger.exception("query failed for payload: %s", payload)
                result = {"error": str(ex)}
            await self._maybe_reply(message, result)

    @staticmethod
    def _parse_body(message: AbstractIncomingMessage) -> dict[str, Any] | None:
        try:
            return json.loads(message.body.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError):
            logger.exception("Could not decode message body as JSON")
            return None

    async def _maybe_reply(
        self, message: AbstractIncomingMessage, body: Any
    ) -> None:
        if not message.reply_to or self._channel is None:
            return
        await self._channel.default_exchange.publish(
            aio_pika.Message(
                body=json.dumps(body).encode("utf-8"),
                correlation_id=message.correlation_id,
                content_type="application/json",
            ),
            routing_key=message.reply_to,
        )

    async def stop(self) -> None:
        if self._connection is not None:
            await self._connection.close()
