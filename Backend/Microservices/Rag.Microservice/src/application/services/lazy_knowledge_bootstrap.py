"""Run-once, awaitable wrapper around `KnowledgeBootstrapService`.

`ensure_ready()` is idempotent and **blocking** — the first caller schedules
the bootstrap and awaits it; every subsequent caller awaits the same task and
returns the moment it completes. After completion, calls return instantly.

Designed for transport handlers (RabbitMQ consumer, gRPC servicer) to `await`
at the start of every request, so:

  - Service startup is fast (no bootstrap-on-boot)
  - The first incoming request fires bootstrap and **waits** for it before any
    ingest/query work proceeds — no LLM/image-gen call ever runs against a
    half-built knowledge index
  - Concurrent first-wave requests all await the same in-flight task (no
    duplicate bootstraps)
  - Once bootstrap is done, every subsequent request hits an already-complete
    task and is effectively a no-op

The check-and-set on `_task` is sync (no `await` between read and write), so
it's safe under asyncio's cooperative scheduling — no Lock needed.
"""
from __future__ import annotations

import asyncio
import logging

from .knowledge_bootstrap_service import KnowledgeBootstrapService

logger = logging.getLogger("rag-service.knowledge-bootstrap")


class LazyKnowledgeBootstrap:
    def __init__(self, bootstrap: KnowledgeBootstrapService) -> None:
        self._service = bootstrap
        self._task: asyncio.Task[None] | None = None

    async def ensure_ready(self) -> None:
        """Idempotent + blocking. First caller schedules + awaits the bootstrap;
        subsequent callers await the same task. After completion returns instantly."""
        if self._task is None:
            self._task = asyncio.create_task(self._run())
        await self._task

    def mark_already_ready(self) -> None:
        """Pre-set the bootstrap as completed without running it. Used by the
        entrypoint when the baked-knowledge seed is in sync with the live
        Qdrant — the registry is already populated and any walk of
        `src/knowledge/*.md` would just ack every section as 'skipped'.

        Idempotent: a no-op when `ensure_ready()` already triggered, or when
        this was already called. Subsequent `await ensure_ready()` calls then
        return immediately on the next event-loop tick.
        """
        if self._task is not None:
            return

        async def _noop() -> None:
            logger.info("Knowledge bootstrap pre-marked ready (seed already in sync)")

        self._task = asyncio.create_task(_noop())

    @property
    def task(self) -> asyncio.Task[None] | None:
        """The bootstrap task, or None if `ensure_ready()` was never called.
        Used by the entrypoint to cancel cleanly on shutdown."""
        return self._task

    async def _run(self) -> None:
        try:
            logger.info("Lazy knowledge bootstrap fired by first incoming request")
            counts = await self._service.bootstrap()
            total = sum(counts.values())
            logger.info(
                "Lazy knowledge bootstrap complete: %d new/updated docs across %d namespaces",
                total, len(counts),
            )
        except Exception:
            logger.exception("Lazy knowledge bootstrap failed")
            raise
