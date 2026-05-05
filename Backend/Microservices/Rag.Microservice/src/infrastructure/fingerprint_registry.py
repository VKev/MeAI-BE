"""In-memory `dict[str, str]` of (document_id → fingerprint), persisted to a
JSON file on every record. Implements `FingerprintRegistry` Protocol.

Persistence is naive (full re-write per change). At our scale (<10k docs per
account, single-process) this is fine. If we ever need higher write throughput,
swap this implementation for a Redis or SQLite-backed one without touching the
services that depend on the Protocol.
"""
from __future__ import annotations

import asyncio
import json
import logging
from pathlib import Path
from typing import Any

logger = logging.getLogger("rag-service.fingerprint-registry")


class JsonFileFingerprintRegistry:
    """JSON-file-persisted fingerprint registry with optional pre-delete callback.

    The pre-delete callback is invoked when `reconcile()` decides the existing
    fingerprint differs (status="updated"). The IngestService passes the
    LightRAG facade's `delete_by_document_id` here so the upcoming re-insert
    replaces the doc cleanly. Callback runs only for the LightRAG-backed kinds
    (text/image); image_native and video have their own delete-before-upsert
    paths that don't go through this hook.
    """

    def __init__(self, file_path: Path) -> None:
        self._file = file_path
        self._lock = asyncio.Lock()
        self._data: dict[str, str] = {}
        self._loaded = False

    def load_sync(self) -> None:
        """Eager load on startup. Idempotent."""
        if self._loaded:
            return
        if self._file.exists():
            try:
                raw = json.loads(self._file.read_text(encoding="utf-8"))
                if isinstance(raw, dict):
                    self._data = {str(k): str(v) for k, v in raw.items()}
            except Exception:
                logger.exception(
                    "Failed to load %s; starting with empty registry", self._file
                )
                self._data = {}
        self._loaded = True

    async def reconcile(
        self, document_id: str | None, fingerprint: str | None
    ) -> str:
        if not document_id or not fingerprint:
            return "ingested"
        existing = self._data.get(document_id)
        if existing is None:
            return "ingested"
        if existing == fingerprint:
            return "skipped"
        return "updated"

    async def record(
        self, document_id: str | None, fingerprint: str | None
    ) -> None:
        if not document_id:
            return
        self._data[document_id] = fingerprint or ""
        await self._persist()

    async def _persist(self) -> None:
        async with self._lock:
            try:
                self._file.write_text(
                    json.dumps(self._data, ensure_ascii=False, indent=2),
                    encoding="utf-8",
                )
            except Exception:
                logger.exception("Failed to persist %s", self._file)

    def list_with_prefix(self, prefix: str) -> dict[str, str]:
        if not prefix:
            return dict(self._data)
        return {
            doc_id: fp
            for doc_id, fp in self._data.items()
            if doc_id.startswith(prefix)
        }

    def matching_ids(self, prefix: str) -> list[str]:
        if not prefix:
            return list(self._data.keys())
        return [doc_id for doc_id in self._data if doc_id.startswith(prefix)]
