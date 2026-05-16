"""Ingest orchestration. Dispatches by `DocumentKind` to the right pipeline.

Replaces the per-kind `_ingest_*` methods on the old `RagEngine` god-class.
Depends ONLY on Protocols from `src.application.ports` — no Qdrant / OpenAI /
S3 imports here.
"""
from __future__ import annotations

import logging
from typing import Any

from ..ports import (
    FingerprintRegistry,
    ImageMirrorPort,
    LightRagFacade,
    MultimodalEmbedderPort,
    VideoRagEnginePort,
    VisionDescriberPort,
    VisualStorePort,
)

logger = logging.getLogger("rag-service.ingest")


class IngestService:
    """Per-document ingest dispatch.

    Each call to `ingest_one(payload)` is independent (no batching at this layer
    — batching is handled at the gRPC/RMQ transport layer for parallelism).

    Dependencies are all Protocols. Some are optional: `multimodal_embedder` /
    `visual_store` are required only for `kind=image_native`; `video_rag` only
    for `kind=video`. We pass them as nullable and validate at dispatch time
    to keep the constructor honest.
    """

    def __init__(
        self,
        *,
        fingerprints: FingerprintRegistry,
        light_rag: LightRagFacade,
        vision_describer: VisionDescriberPort,
        multimodal_embedder: MultimodalEmbedderPort | None,
        visual_store: VisualStorePort | None,
        image_mirror: ImageMirrorPort,
        video_rag: VideoRagEnginePort | None,
    ) -> None:
        self._fingerprints = fingerprints
        self._light_rag = light_rag
        self._vision = vision_describer
        self._embedder = multimodal_embedder
        self._visual = visual_store
        self._mirror = image_mirror
        self._video = video_rag

    async def ingest_one(self, payload: dict[str, Any]) -> dict[str, Any]:
        """Single-doc ingest. Wire format kept loose (`dict`) so we can accept
        both RMQ JSON payloads and gRPC-derived dicts without an extra mapping
        layer in transport.
        """
        kind = (payload.get("kind") or "text").lower()
        document_id = payload.get("documentId") or payload.get("document_id")
        fingerprint = payload.get("fingerprint")

        # Reconcile against the in-memory registry. Three outcomes:
        #   skipped  → fingerprint matches → no-op
        #   ingested → not seen before → caller proceeds
        #   updated  → fingerprint differs → caller deletes existing then inserts
        action = await self._fingerprints.reconcile(document_id, fingerprint)
        if action == "skipped":
            return {"status": "skipped", "documentId": document_id, "kind": kind}

        if action == "updated" and document_id:
            try:
                await self._light_rag.delete_by_document_id(document_id)
            except Exception:
                # We log but don't fail — re-insert below replaces the doc and
                # any stale entity edges decay through normal LightRAG hygiene.
                logger.exception(
                    "Failed to delete existing doc %s before update; proceeding with re-insert",
                    document_id,
                )

        if kind == "text":
            return await self._ingest_text(payload, document_id, fingerprint, action)
        if kind == "texts":
            return await self._ingest_texts(payload, document_id)
        if kind == "image":
            return await self._ingest_image(payload, document_id, fingerprint, action)
        if kind == "image_native":
            return await self._ingest_image_native(payload, document_id, fingerprint, action)
        if kind == "video":
            return await self._ingest_video(payload, document_id, fingerprint, action)

        raise ValueError(
            f"Unsupported ingest kind: {kind!r} "
            f"(supported: text, texts, image, image_native, video)"
        )

    # ── per-kind handlers ──────────────────────────────────────────────────

    async def _ingest_text(
        self,
        payload: dict[str, Any],
        document_id: str | None,
        fingerprint: str | None,
        action: str,
    ) -> dict[str, Any]:
        content = payload.get("content")
        if not content:
            raise ValueError("'content' is required for kind=text")
        await self._light_rag.insert_text(
            content,
            document_id=document_id,
            file_path=document_id or "text-input",
        )
        await self._fingerprints.record(document_id, fingerprint)
        return {"status": action, "documentId": document_id, "kind": "text"}

    async def _ingest_texts(
        self,
        payload: dict[str, Any],
        document_id: str | None,
    ) -> dict[str, Any]:
        items = payload.get("content") or []
        if not isinstance(items, list) or not items:
            raise ValueError("'content' must be a non-empty list for kind=texts")
        await self._light_rag.insert_text(items)
        return {
            "status": "ingested",
            "documentId": document_id,
            "kind": "texts",
            "count": len(items),
        }

    async def _ingest_image(
        self,
        payload: dict[str, Any],
        document_id: str | None,
        fingerprint: str | None,
        action: str,
    ) -> dict[str, Any]:
        image_url = payload.get("imageUrl") or payload.get("image_url")
        image_b64 = payload.get("imageBase64") or payload.get("image_base64")
        if not image_url and not image_b64:
            raise ValueError("'imageUrl' or 'imageBase64' is required for kind=image")

        if image_url:
            image_ref = image_url
        else:
            mime = payload.get("mimeType") or payload.get("mime_type") or "image/jpeg"
            image_ref = f"data:{mime};base64,{image_b64}"

        description = await self._vision.describe(
            image_ref, custom_prompt=payload.get("describePrompt")
        )

        caption = payload.get("caption") or ""
        prefix = f"[Image: {caption}] " if caption else "[Image] "
        text = prefix + description

        await self._light_rag.insert_text(
            text,
            document_id=document_id,
            file_path=document_id or "image-input",
        )
        await self._fingerprints.record(document_id, fingerprint)
        return {
            "status": action,
            "documentId": document_id,
            "kind": "image",
            "caption": caption or None,
            "description": description,
        }

    async def _ingest_image_native(
        self,
        payload: dict[str, Any],
        document_id: str | None,
        fingerprint: str | None,
        action: str,
    ) -> dict[str, Any]:
        if self._embedder is None or self._visual is None:
            raise RuntimeError("Multimodal embedder / visual store not initialized")
        if not document_id:
            raise ValueError("'documentId' is required for kind=image_native")

        image_url = payload.get("imageUrl") or payload.get("image_url")
        if not image_url:
            raise ValueError("'imageUrl' is required for kind=image_native")

        caption = payload.get("caption")
        scope = payload.get("scope") or payload.get("documentIdPrefix") or ""
        post_id = payload.get("postId") or payload.get("post_id")

        # Wipe prior points for this doc_id so caption-presence changes don't orphan rows.
        await self._visual.delete_by_document_id(document_id)

        # ALWAYS mirror to S3 first — Vertex AI + OpenAI both refuse FB CDN URLs
        # via robots.txt. The S3 key is stored in the Qdrant payload; presigned
        # URLs are generated on every retrieval (avoids the 7-day expiry trap).
        mirror_key = await self._mirror.upload(
            image_url, scope_hash=scope, doc_id=document_id
        )
        embed_url = self._mirror.presign(mirror_key) if mirror_key else image_url
        if mirror_key is None:
            logger.warning(
                "S3 mirror failed for %s — falling back to original URL for embed (likely to fail)",
                document_id,
            )

        common_payload = {
            "image_url": image_url,
            "mirror_s3_key": mirror_key,
            "caption": caption,
            "post_id": post_id,
            "fingerprint": fingerprint or "",
        }

        any_success = False
        failure_reasons: list[str] = []
        try:
            image_vec = await self._embedder.embed_image(embed_url, caption)
            await self._visual.upsert_point(
                document_id=document_id,
                kind="image",
                vector=image_vec,
                scope=scope,
                payload=common_payload,
            )
            any_success = True
        except Exception as ex:
            reason = str(ex)
            failure_reasons.append(f"image embedding: {reason[:500]}")
            logger.warning(
                "Image embedding failed for %s (%s); will be retried on next /index",
                document_id,
                reason[:240],
            )

        if caption:
            try:
                caption_vec = await self._embedder.embed_text(caption)
                await self._visual.upsert_point(
                    document_id=document_id,
                    kind="caption",
                    vector=caption_vec,
                    scope=scope,
                    payload=common_payload,
                )
                any_success = True
            except Exception as ex:
                reason = str(ex)
                failure_reasons.append(f"caption embedding: {reason[:500]}")
                logger.warning(
                    "Caption embedding failed for %s (%s); will be retried on next /index",
                    document_id,
                    reason[:240],
                )

        if not any_success:
            # Don't record fingerprint — next index attempt re-tries.
            joined_reasons = "; ".join(failure_reasons) or "unknown provider error"
            raise RuntimeError(
                f"All multimodal embeddings failed for {document_id}: {joined_reasons[:900]}"
            )

        await self._fingerprints.record(document_id, fingerprint)
        return {
            "status": action,
            "documentId": document_id,
            "kind": "image_native",
            "scope": scope,
        }

    async def _ingest_video(
        self,
        payload: dict[str, Any],
        document_id: str | None,
        fingerprint: str | None,
        action: str,
    ) -> dict[str, Any]:
        if self._video is None:
            raise RuntimeError("VideoRAG is disabled (set VIDEORAG_ENABLED=1)")
        result = await self._video.ingest_video(payload)
        await self._fingerprints.record(document_id, fingerprint)
        result["status"] = action
        return result
