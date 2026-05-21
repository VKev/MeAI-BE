"""Query orchestration. Three operations:
  - text query (LightRAG hybrid retrieval, optional answer synthesis)
  - multimodal query (text + visual + video legs, no answer synthesis here)
  - list fingerprints (for the .NET indexer to see what's already ingested)

Replaces `RagEngine.query` / `multimodal_query` / `list_fingerprints`.
"""
from __future__ import annotations

import logging
import re
from typing import Any

from ..ports import (
    FingerprintRegistry,
    LightRagFacade,
    MultimodalEmbedderPort,
    VideoRagEnginePort,
    VisualStorePort,
)

logger = logging.getLogger("rag-service.query")

_CAPTION_RE = re.compile(
    r"(?:^|\n)Caption:\s*(?P<caption>.*?)(?=\n[A-Za-z][A-Za-z0-9 ]{0,40}:|\Z)",
    re.DOTALL,
)


class QueryService:
    def __init__(
        self,
        *,
        fingerprints: FingerprintRegistry,
        light_rag: LightRagFacade,
        multimodal_embedder: MultimodalEmbedderPort | None,
        visual_store: VisualStorePort | None,
        video_rag: VideoRagEnginePort | None,
    ) -> None:
        self._fingerprints = fingerprints
        self._light_rag = light_rag
        self._embedder = multimodal_embedder
        self._visual = visual_store
        self._video = video_rag

    # ── multimodal_query ───────────────────────────────────────────────────

    async def multimodal_query(self, payload: dict[str, Any]) -> dict[str, Any]:
        """Hybrid retrieval. Returns ranked hits per mode — caller fuses.

        Wire shape preserved EXACTLY as the old `RagEngine.multimodal_query`
        produced. Field names are camelCase + snake_case alternates so both
        .NET (camel) and Python (snake) callers parse cleanly.
        """
        query_text = payload.get("query")
        if not query_text:
            raise ValueError("'query' is required")

        modes = payload.get("modes") or ["text", "visual"]
        if isinstance(modes, str):
            modes = [m.strip() for m in modes.split(",") if m.strip()]
        top_k = int(payload.get("topK") or payload.get("top_k") or 8)
        document_id_prefix = (
            payload.get("documentIdPrefix") or payload.get("document_id_prefix") or ""
        )

        result: dict[str, Any] = {
            "query": query_text,
            "topK": top_k,
            "documentIdPrefix": document_id_prefix,
        }

        if "text" in modes:
            result["text"] = await self._text_leg(query_text, document_id_prefix, top_k)

        if "visual" in modes:
            result["visual"] = await self._visual_leg(query_text, document_id_prefix, top_k, result)

        if "video" in modes:
            result["video"] = await self._video_leg(query_text, payload, top_k, result)

        return result

    async def _text_leg(
        self, query_text: str, document_id_prefix: str, top_k: int
    ) -> dict[str, Any]:
        matched_ids: list[str] | None = None
        text_context = ""
        reference_docs: list[dict[str, Any]] = []
        if document_id_prefix:
            matched_ids = self._fingerprints.matching_ids(document_id_prefix)

        if matched_ids is None or matched_ids:
            try:
                text_result = await self._light_rag.query_with_references(
                    query_text,
                    mode="hybrid",
                    top_k=top_k,
                    only_need_context=True,
                    ids=matched_ids,
                )
                text_context = str(text_result.get("content") or "")
                reference_ids = _reference_document_ids(
                    text_result.get("references"),
                    text_result.get("raw_data"),
                    matched_ids or [],
                    document_id_prefix,
                    top_k,
                )
                reference_docs = await self._build_text_references(reference_ids)
            except Exception as ex:
                logger.exception("LightRAG context query failed")
                text_context = f"(context retrieval failed: {ex})"

        return {
            "context": text_context,
            "matchedDocumentIds": [doc["documentId"] for doc in reference_docs],
            "references": reference_docs,
        }

    async def _build_text_references(
        self, document_ids: list[str]
    ) -> list[dict[str, Any]]:
        docs = await self._light_rag.get_full_documents(document_ids)
        references: list[dict[str, Any]] = []
        for doc_id in document_ids:
            doc = docs.get(doc_id) or {}
            content = str(doc.get("content") or "").strip()
            references.append({
                "documentId": doc_id,
                "postId": _post_id_from_document_id(doc_id),
                "content": content or None,
                "caption": _extract_caption(content),
            })
        return references

    async def _visual_leg(
        self,
        query_text: str,
        document_id_prefix: str,
        top_k: int,
        result_for_errors: dict[str, Any],
    ) -> list[dict[str, Any]]:
        if self._embedder is None or self._visual is None:
            return []
        try:
            qvec = await self._embedder.embed_text(query_text)
            return await self._visual.search(
                vector=qvec,
                top_k=top_k,
                scope=document_id_prefix or None,
            )
        except Exception as ex:
            logger.exception("Multimodal visual search failed")
            result_for_errors["visualError"] = str(ex)
            return []

    async def _video_leg(
        self,
        query_text: str,
        payload: dict[str, Any],
        top_k: int,
        result_for_errors: dict[str, Any],
    ) -> list[dict[str, Any]]:
        if self._video is None:
            result_for_errors["videoError"] = "VideoRAG disabled"
            return []
        platform = (payload.get("platform") or "").lower()
        social_media_id = (
            payload.get("socialMediaId") or payload.get("social_media_id") or ""
        )
        if not (platform and social_media_id):
            result_for_errors["videoError"] = (
                "video mode requires platform + socialMediaId in payload"
            )
            return []
        try:
            scope = self._video.scope_for(platform, social_media_id)
            raw_hits = await self._video.query_video(
                query_text,
                platform=platform,
                social_media_id=social_media_id,
                top_k=top_k,
            )
            return await self._video.hydrate_segments(scope, raw_hits)
        except Exception as ex:
            logger.exception("VideoRAG video search failed")
            result_for_errors["videoError"] = str(ex)
            return []

    # ── plain text query ───────────────────────────────────────────────────

    async def text_query(self, payload: dict[str, Any]) -> dict[str, Any]:
        query_text = payload.get("query")
        if not query_text:
            raise ValueError("'query' is required")
        mode = (payload.get("mode") or "hybrid").lower()
        top_k = int(payload.get("topK") or payload.get("top_k") or 10)
        only_context = bool(
            payload.get("onlyNeedContext") or payload.get("only_need_context")
        )
        document_id_prefix = (
            payload.get("documentIdPrefix") or payload.get("document_id_prefix")
        )

        matched_ids: list[str] | None = None
        if document_id_prefix:
            matched_ids = self._fingerprints.matching_ids(document_id_prefix)
            if not matched_ids:
                return {
                    "query": query_text,
                    "mode": mode,
                    "topK": top_k,
                    "answer": "",
                    "matchedDocumentIds": [],
                }

        answer = await self._light_rag.query(
            query_text,
            mode=mode,
            top_k=top_k,
            only_need_context=only_context,
            ids=matched_ids,
        )
        return {
            "query": query_text,
            "mode": mode,
            "topK": top_k,
            "answer": answer,
            "matchedDocumentIds": matched_ids,
        }

    # ── list_fingerprints ──────────────────────────────────────────────────

    async def list_fingerprints(self, payload: dict[str, Any]) -> dict[str, Any]:
        prefix = (
            payload.get("documentIdPrefix")
            or payload.get("document_id_prefix")
            or ""
        )
        matches = self._fingerprints.list_with_prefix(prefix)
        return {"fingerprints": matches, "count": len(matches)}


_REFERENCE_ID_KEYS = {
    "documentId",
    "document_id",
    "doc_id",
    "full_doc_id",
    "file_path",
    "source_id",
    "id",
}


def _reference_document_ids(
    references: Any,
    raw_data: Any,
    fallback_ids: list[str],
    prefix: str,
    top_k: int,
) -> list[str]:
    candidates: list[str] = []
    _collect_reference_ids(references, prefix, candidates)
    _collect_reference_ids(raw_data, prefix, candidates)
    if not candidates:
        candidates.extend(fallback_ids)

    seen: set[str] = set()
    ordered: list[str] = []
    for doc_id in candidates:
        if doc_id in seen or not _is_primary_post_document(doc_id, prefix):
            continue
        seen.add(doc_id)
        ordered.append(doc_id)
        if len(ordered) >= top_k:
            break
    return ordered


def _collect_reference_ids(value: Any, prefix: str, candidates: list[str]) -> None:
    if value is None:
        return

    if isinstance(value, str):
        doc_id = _normalise_reference_id(value, prefix)
        if doc_id:
            candidates.append(doc_id)
        return

    if isinstance(value, dict):
        for key in _REFERENCE_ID_KEYS:
            if key in value:
                _collect_reference_ids(value.get(key), prefix, candidates)
        for child in value.values():
            _collect_reference_ids(child, prefix, candidates)
        return

    if isinstance(value, (list, tuple)):
        for item in value:
            _collect_reference_ids(item, prefix, candidates)


def _normalise_reference_id(value: str, prefix: str) -> str | None:
    value = value.strip().strip("\"'`[](){}<>.,;")
    if not value:
        return None

    if prefix:
        start = value.find(prefix)
        if start < 0:
            return None
        value = value[start:]

    for sep in (" ", "\t", "\r", "\n"):
        idx = value.find(sep)
        if idx > 0:
            value = value[:idx]
    return value.strip().strip("\"'`[](){}<>.,;") or None


def _is_primary_post_document(doc_id: str, prefix: str) -> bool:
    if prefix and not doc_id.startswith(prefix):
        return False
    tail = doc_id[len(prefix):] if prefix else doc_id
    if not tail or tail == "profile":
        return False
    return ":" not in tail


def _post_id_from_document_id(doc_id: str) -> str | None:
    parts = doc_id.split(":", 2)
    if len(parts) < 3:
        return None
    post_id = parts[2].split(":", 1)[0]
    if not post_id or post_id == "profile":
        return None
    return post_id


def _extract_caption(content: str) -> str | None:
    if not content:
        return None
    match = _CAPTION_RE.search(content)
    if match:
        caption = match.group("caption").strip()
        return caption or None
    return None
