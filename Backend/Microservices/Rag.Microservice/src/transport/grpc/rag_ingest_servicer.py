"""Synchronous gRPC ingest server. Bridges `IngestBatch` RPC → `IngestService`.

The proto contract preserved EXACTLY — same message shapes, same model name,
same status strings ("ingested" / "updated" / "unchanged" / "failed").
"""
from __future__ import annotations

import logging
from typing import Any

import grpc

from .grpc_gen import rag_service_pb2 as pb
from .grpc_gen import rag_service_pb2_grpc as pb_grpc
from ...application.services.ingest_service import IngestService
from ...application.services.lazy_knowledge_bootstrap import LazyKnowledgeBootstrap

logger = logging.getLogger("rag-service.grpc")


class RagIngestServicer(pb_grpc.RagIngestServiceServicer):
    """gRPC adapter — delegates to `IngestService`."""

    def __init__(
        self,
        ingest: IngestService,
        lazy_bootstrap: LazyKnowledgeBootstrap,
    ) -> None:
        self._ingest = ingest
        self._lazy_bootstrap = lazy_bootstrap

    async def IngestBatch(  # noqa: N802 — gRPC method name fixed by proto
        self,
        request: pb.IngestBatchRequest,
        context: grpc.aio.ServicerContext,
    ) -> pb.IngestBatchResponse:
        # Block until knowledge bootstrap is ready. First caller does the work;
        # subsequent callers await the same already-completed task → instant.
        await self._lazy_bootstrap.ensure_ready()
        total = len(request.documents)
        logger.info("gRPC IngestBatch received %d docs", total)

        results: list[pb.IngestResult] = []
        for doc in request.documents:
            payload = self._doc_to_payload(doc)
            try:
                outcome = await self._ingest.ingest_one(payload)
                raw_status = (outcome or {}).get("status") or "ingested"
                # Wire convention: .NET caller expects "unchanged" not "skipped".
                status = "unchanged" if raw_status == "skipped" else raw_status
                fingerprint = (outcome or {}).get("fingerprint") or doc.fingerprint or ""
                results.append(pb.IngestResult(
                    document_id=doc.document_id,
                    status=status,
                    fingerprint=fingerprint,
                    error="",
                ))
            except Exception as ex:
                logger.exception("Ingest failed for doc_id=%s", doc.document_id)
                results.append(pb.IngestResult(
                    document_id=doc.document_id,
                    status="failed",
                    fingerprint=doc.fingerprint or "",
                    error=str(ex)[:500],
                ))

        ingested = sum(1 for r in results if r.status in ("ingested", "updated"))
        unchanged = sum(1 for r in results if r.status == "unchanged")
        failed = sum(1 for r in results if r.status == "failed")
        logger.info(
            "gRPC IngestBatch done: total=%d ingested/updated=%d unchanged=%d failed=%d",
            total, ingested, unchanged, failed,
        )
        return pb.IngestBatchResponse(results=results)

    @staticmethod
    def _doc_to_payload(doc: pb.IngestDocument) -> dict[str, Any]:
        """Convert proto message → dict shape that `IngestService.ingest_one`
        expects. Empty proto strings become None so `IngestService` branching
        sees them as missing.
        """
        def _opt(v: str) -> str | None:
            return v if v else None

        return {
            "kind": doc.kind,
            "documentId": doc.document_id,
            "fingerprint": doc.fingerprint,
            "content": _opt(doc.content),
            "imageUrl": _opt(doc.image_url),
            "caption": _opt(doc.caption),
            "describePrompt": _opt(doc.describe_prompt),
            "scope": _opt(doc.scope),
            "postId": _opt(doc.post_id),
            "platform": _opt(doc.platform),
            "socialMediaId": _opt(doc.social_media_id),
            "videoUrl": _opt(doc.video_url),
        }


class GrpcServer:
    """Lifecycle wrapper around an aio grpc.Server."""

    def __init__(
        self,
        ingest: IngestService,
        lazy_bootstrap: LazyKnowledgeBootstrap,
        port: int = 5006,
    ) -> None:
        self._ingest = ingest
        self._lazy_bootstrap = lazy_bootstrap
        self._port = port
        self._server: grpc.aio.Server | None = None

    async def start(self) -> None:
        options = [
            ("grpc.max_receive_message_length", 50 * 1024 * 1024),
            ("grpc.max_send_message_length", 50 * 1024 * 1024),
        ]
        self._server = grpc.aio.server(options=options)
        pb_grpc.add_RagIngestServiceServicer_to_server(
            RagIngestServicer(self._ingest, self._lazy_bootstrap), self._server,
        )
        self._server.add_insecure_port(f"[::]:{self._port}")
        await self._server.start()
        logger.info("gRPC server listening on :%d", self._port)

    async def stop(self) -> None:
        if self._server is not None:
            await self._server.stop(grace=10)
