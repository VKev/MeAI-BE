"""Boot-time bootstrap of the global knowledge base into LightRAG.

Reads `*.md` files from the knowledge directory (via `KnowledgeLoaderPort`),
parses each into `## ` sections, and feeds them through `IngestService` as
text docs. Idempotent thanks to fingerprint reconciliation in IngestService.
"""
from __future__ import annotations

import logging

from ..ports import KnowledgeLoaderPort
from .ingest_service import IngestService

logger = logging.getLogger("rag-service.knowledge-bootstrap")


class KnowledgeBootstrapService:
    def __init__(
        self,
        *,
        loader: KnowledgeLoaderPort,
        ingest: IngestService,
    ) -> None:
        self._loader = loader
        self._ingest = ingest

    async def bootstrap(self) -> dict[str, int]:
        """Returns `{namespace: new_or_updated_count}`."""
        counts: dict[str, int] = {}
        for namespace, file_path in self._loader.list_namespaces():
            try:
                docs = self._loader.parse_namespace(namespace, file_path)
            except Exception:
                logger.exception("Failed to parse %s — skipping", file_path)
                continue

            new_or_updated = 0
            for doc in docs:
                try:
                    result = await self._ingest.ingest_one({
                        "kind": "text",
                        "documentId": doc.document_id,
                        "fingerprint": doc.fingerprint,
                        "content": doc.content,
                    })
                    if result.get("status") in ("ingested", "updated"):
                        new_or_updated += 1
                except Exception:
                    logger.exception("Failed to ingest %s — continuing", doc.document_id)
            counts[namespace] = new_or_updated
            logger.info(
                "Knowledge bootstrap '%s': %d/%d sections ingested or updated",
                namespace, new_or_updated, len(docs),
            )
        return counts
