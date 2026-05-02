"""LightRAG engine wired with OpenRouter LLM/embeddings + Qdrant vector store.

We use LightRAG directly (the engine that powers RAG-Anything) instead of the
full raganything package to keep the image lean. The raganything source is still
present in the repo for future multimodal use; switching is a code change in
this file.
"""
from __future__ import annotations

import asyncio
import json
import logging
import os
from pathlib import Path

from lightrag import LightRAG, QueryParam
from lightrag.kg.shared_storage import initialize_pipeline_status
from lightrag.llm.openai import openai_complete_if_cache, openai_embed
from lightrag.utils import EmbeddingFunc, setup_logger

from .config import Config
from .multimodal_embedder import MultimodalEmbedder
from .qdrant_visual_store import QdrantVisualStore

logger = logging.getLogger("rag-service.engine")


class RagEngine:
    def __init__(self, cfg: Config) -> None:
        self.cfg = cfg
        self.rag: LightRAG | None = None
        # documentId -> fingerprint. Persisted to disk for skip/update detection.
        self._ingested: dict[str, str] = {}
        self._ids_file: Path | None = None
        self._ids_lock = asyncio.Lock()
        self.multimodal_embedder: MultimodalEmbedder | None = None
        self.visual_store: QdrantVisualStore | None = None

    async def initialize(self) -> None:
        os.makedirs(self.cfg.working_dir, exist_ok=True)
        self._ids_file = Path(self.cfg.working_dir) / "ingested_ids.json"
        self._load_ids()

        # LightRAG's Qdrant adapter reads these from the environment at construction time.
        os.environ["QDRANT_URL"] = self.cfg.qdrant_url
        if self.cfg.qdrant_api_key:
            os.environ["QDRANT_API_KEY"] = self.cfg.qdrant_api_key

        setup_logger("lightrag", level=self.cfg.log_level)

        async def llm_func(
            prompt: str,
            system_prompt: str | None = None,
            history_messages: list | None = None,
            **kwargs,
        ) -> str:
            return await openai_complete_if_cache(
                self.cfg.llm_model,
                prompt,
                system_prompt=system_prompt,
                history_messages=history_messages or [],
                api_key=self.cfg.llm_api_key,
                base_url=self.cfg.llm_base_url,
                **kwargs,
            )

        async def embed_func(texts: list[str]):
            return await openai_embed(
                texts,
                model=self.cfg.embed_model,
                api_key=self.cfg.embed_api_key,
                base_url=self.cfg.embed_base_url,
            )

        embedding = EmbeddingFunc(
            embedding_dim=self.cfg.embed_dim,
            max_token_size=self.cfg.embed_max_tokens,
            func=embed_func,
        )

        self.rag = LightRAG(
            working_dir=self.cfg.working_dir,
            llm_model_func=llm_func,
            embedding_func=embedding,
            vector_storage="QdrantVectorDBStorage",
            workspace=self.cfg.qdrant_namespace,
        )

        await self.rag.initialize_storages()
        await initialize_pipeline_status()
        logger.info(
            "RAG engine ready (llm=%s embed=%s qdrant=%s namespace=%s)",
            self.cfg.llm_model,
            self.cfg.embed_model,
            self.cfg.qdrant_url,
            self.cfg.qdrant_namespace,
        )

        # Multimodal (image + text) embedder via OpenRouter; backed by a separate
        # Qdrant collection so we don't fight LightRAG's text-only retrieval graph.
        self.multimodal_embedder = MultimodalEmbedder(
            base_url=self.cfg.multimodal_embed_base_url,
            api_key=self.cfg.multimodal_embed_api_key,
            model=self.cfg.multimodal_embed_model,
            max_concurrency=self.cfg.multimodal_embed_max_concurrency,
        )
        self.visual_store = QdrantVisualStore(
            url=self.cfg.qdrant_url,
            api_key=self.cfg.qdrant_api_key,
            collection=self.cfg.multimodal_visual_collection,
        )
        await self.visual_store.initialize(self.cfg.multimodal_embed_dim)
        logger.info(
            "Multimodal path ready (model=%s dim=%d collection=%s)",
            self.cfg.multimodal_embed_model,
            self.cfg.multimodal_embed_dim,
            self.cfg.multimodal_visual_collection,
        )

    async def ingest(self, payload: dict) -> dict:
        if self.rag is None:
            raise RuntimeError("Engine not initialized")

        kind = (payload.get("kind") or "text").lower()
        document_id = payload.get("documentId") or payload.get("document_id")
        fingerprint = payload.get("fingerprint")

        # Idempotency: skip when fingerprint matches what's already indexed.
        # If fingerprint differs, delete the existing doc so the re-insert replaces it.
        action = await self._reconcile(document_id, fingerprint)
        if action == "skipped":
            return {"status": "skipped", "documentId": document_id, "kind": kind}

        if kind == "text":
            content = payload.get("content")
            if not content:
                raise ValueError("'content' is required for kind=text")
            await self.rag.ainsert(content, ids=document_id, file_paths=document_id or "text-input")
            await self._record_id(document_id, fingerprint)
            return {"status": action, "documentId": document_id, "kind": kind}

        if kind == "texts":
            items = payload.get("content") or []
            if not isinstance(items, list) or not items:
                raise ValueError("'content' must be a non-empty list for kind=texts")
            await self.rag.ainsert(items)
            return {"status": "ingested", "documentId": document_id, "kind": kind, "count": len(items)}

        if kind == "image":
            result = await self._ingest_image(payload, document_id)
            await self._record_id(document_id, fingerprint)
            result["status"] = action
            return result

        if kind == "image_native":
            result = await self._ingest_image_native(payload, document_id, fingerprint)
            await self._record_id(document_id, fingerprint)
            result["status"] = action
            return result

        raise ValueError(
            f"Unsupported ingest kind: {kind!r} (supported: text, texts, image, image_native)"
        )

    async def _ingest_image_native(
        self,
        payload: dict,
        document_id: str | None,
        fingerprint: str | None,
    ) -> dict:
        if self.multimodal_embedder is None or self.visual_store is None:
            raise RuntimeError("Multimodal embedder not initialized")
        if not document_id:
            raise ValueError("'documentId' is required for kind=image_native")

        image_url = payload.get("imageUrl") or payload.get("image_url")
        if not image_url:
            raise ValueError("'imageUrl' is required for kind=image_native")

        caption = payload.get("caption")
        scope = payload.get("scope") or payload.get("documentIdPrefix") or ""
        post_id = payload.get("postId") or payload.get("post_id")

        # Wipe any prior points for this doc_id so caption presence changes don't orphan.
        await self.visual_store.delete_by_document_id(document_id)

        # OpenRouter free tier is flaky under load — soft-fail individual messages
        # so a single provider hiccup doesn't drop the whole batch. Re-running
        # /index (which only re-queues docs missing from the fingerprint registry)
        # will retry whatever didn't make it.
        any_success = False
        try:
            image_vec = await self.multimodal_embedder.embed_image(image_url, caption)
            await self.visual_store.upsert_point(
                document_id=document_id,
                kind="image",
                vector=image_vec,
                scope=scope,
                payload={
                    "image_url": image_url,
                    "caption": caption,
                    "post_id": post_id,
                    "fingerprint": fingerprint or "",
                },
            )
            any_success = True
        except Exception:
            logger.warning(
                "Image embedding failed for %s; will be retried on next /index", document_id
            )

        if caption:
            try:
                caption_vec = await self.multimodal_embedder.embed_text(caption)
                await self.visual_store.upsert_point(
                    document_id=document_id,
                    kind="caption",
                    vector=caption_vec,
                    scope=scope,
                    payload={
                        "image_url": image_url,
                        "caption": caption,
                        "post_id": post_id,
                        "fingerprint": fingerprint or "",
                    },
                )
                any_success = True
            except Exception:
                logger.warning(
                    "Caption embedding failed for %s; will be retried on next /index",
                    document_id,
                )

        if not any_success:
            # Don't write to the fingerprint registry so the next index attempt re-tries.
            raise RuntimeError(f"All multimodal embeddings failed for {document_id}")

        return {
            "status": "ingested",
            "documentId": document_id,
            "kind": "image_native",
            "scope": scope,
        }

    def _load_ids(self) -> None:
        if self._ids_file is None or not self._ids_file.exists():
            return
        try:
            data = json.loads(self._ids_file.read_text(encoding="utf-8"))
            if isinstance(data, dict):
                self._ingested = {str(k): str(v) for k, v in data.items()}
        except Exception:
            logger.exception("Failed to load %s; starting with empty registry", self._ids_file)
            self._ingested = {}

    async def _persist_ids(self) -> None:
        if self._ids_file is None:
            return
        async with self._ids_lock:
            try:
                self._ids_file.write_text(
                    json.dumps(self._ingested, ensure_ascii=False, indent=2),
                    encoding="utf-8",
                )
            except Exception:
                logger.exception("Failed to persist %s", self._ids_file)

    async def _reconcile(self, document_id: str | None, fingerprint: str | None) -> str:
        """Decide whether this ingest is a no-op, a fresh insert, or an update.

        Returns "skipped" | "ingested" | "updated". When "updated", the existing
        LightRAG doc is deleted up front so the re-insert replaces it cleanly.
        """
        if not document_id or not fingerprint:
            return "ingested"
        existing = self._ingested.get(document_id)
        if existing is None:
            return "ingested"
        if existing == fingerprint:
            return "skipped"
        try:
            await self.rag.adelete_by_doc_id(document_id)
        except Exception:
            logger.exception(
                "Failed to delete existing doc %s before update; proceeding with re-insert",
                document_id,
            )
        return "updated"

    async def _record_id(self, document_id: str | None, fingerprint: str | None) -> None:
        if not document_id:
            return
        self._ingested[document_id] = fingerprint or ""
        await self._persist_ids()

    async def multimodal_query(self, payload: dict) -> dict:
        """Hybrid retrieval: text via LightRAG (context-only) + visual via NVIDIA embedder.

        Returns ranked hits per mode so the caller can fuse and synthesize a
        multimodal answer with their own LLM. No answer synthesis here — the
        Ai service feeds these into gpt-4o-mini multimodal chat.
        """
        if self.rag is None:
            raise RuntimeError("Engine not initialized")

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

        result: dict = {
            "query": query_text,
            "topK": top_k,
            "documentIdPrefix": document_id_prefix,
        }

        # Text mode — return LightRAG's context (no LLM synthesis here) and
        # the matched document IDs from our registry, prefix-filtered.
        if "text" in modes:
            matched_ids: list[str] | None = None
            text_context = ""
            if document_id_prefix:
                matched_ids = [
                    doc_id
                    for doc_id in self._ingested
                    if doc_id.startswith(document_id_prefix)
                ]

            if matched_ids is None or matched_ids:
                param_kwargs: dict = {
                    "mode": "hybrid",
                    "top_k": top_k,
                    "only_need_context": True,
                }
                if matched_ids:
                    param_kwargs["ids"] = matched_ids
                try:
                    param = QueryParam(**param_kwargs)
                except TypeError:
                    param_kwargs.pop("ids", None)
                    param = QueryParam(**param_kwargs)
                try:
                    text_context = await self.rag.aquery(query_text, param=param)
                    if not isinstance(text_context, str):
                        text_context = str(text_context)
                except Exception as ex:
                    logger.exception("LightRAG context query failed")
                    text_context = f"(context retrieval failed: {ex})"

            result["text"] = {
                "context": text_context,
                "matchedDocumentIds": matched_ids or [],
            }

        # Visual mode — embed the text query with the multimodal model and
        # search the visual collection. Cross-modal: text query → image hits.
        if "visual" in modes:
            visual_hits: list[dict] = []
            if self.multimodal_embedder is not None and self.visual_store is not None:
                try:
                    qvec = await self.multimodal_embedder.embed_text(query_text)
                    visual_hits = await self.visual_store.search(
                        vector=qvec,
                        top_k=top_k,
                        scope=document_id_prefix or None,
                    )
                except Exception as ex:
                    logger.exception("Multimodal visual search failed")
                    result["visualError"] = str(ex)
            result["visual"] = visual_hits

        return result

    async def list_fingerprints(self, payload: dict) -> dict:
        prefix = payload.get("documentIdPrefix") or payload.get("document_id_prefix") or ""
        if prefix:
            matches = {doc_id: fp for doc_id, fp in self._ingested.items() if doc_id.startswith(prefix)}
        else:
            matches = dict(self._ingested)
        return {"fingerprints": matches, "count": len(matches)}

    async def _ingest_image(self, payload: dict, document_id: str | None) -> dict:
        image_url = payload.get("imageUrl") or payload.get("image_url")
        image_b64 = payload.get("imageBase64") or payload.get("image_base64")
        if not image_url and not image_b64:
            raise ValueError("'imageUrl' or 'imageBase64' is required for kind=image")

        # OpenAI vision content accepts either a public URL or a `data:` URL.
        if image_url:
            image_ref = image_url
        else:
            mime = payload.get("mimeType") or payload.get("mime_type") or "image/jpeg"
            image_ref = f"data:{mime};base64,{image_b64}"

        description = await self._describe_image(image_ref, payload.get("describePrompt"))

        caption = payload.get("caption") or ""
        prefix = f"[Image: {caption}] " if caption else "[Image] "
        text = prefix + description

        await self.rag.ainsert(
            text,
            ids=document_id,
            file_paths=document_id or "image-input",
        )
        return {
            "status": "ingested",
            "documentId": document_id,
            "kind": "image",
            "caption": caption or None,
            "description": description,
        }

    async def _describe_image(self, image_ref: str, custom_prompt: str | None) -> str:
        """Caption an image using the configured chat model. Requires a vision-capable model."""
        from openai import AsyncOpenAI

        prompt = custom_prompt or (
            "Describe this image thoroughly so it can later be retrieved by semantic search. "
            "Cover: visible text/OCR, objects, scenes, people (no PII guesses), colors, mood, "
            "and any notable details. Be concise but complete."
        )

        client = AsyncOpenAI(api_key=self.cfg.llm_api_key, base_url=self.cfg.llm_base_url)
        response = await client.chat.completions.create(
            model=self.cfg.llm_model,
            messages=[
                {
                    "role": "user",
                    "content": [
                        {"type": "text", "text": prompt},
                        {"type": "image_url", "image_url": {"url": image_ref}},
                    ],
                }
            ],
            max_tokens=600,
        )
        return (response.choices[0].message.content or "").strip()

    async def query(self, payload: dict) -> dict:
        if self.rag is None:
            raise RuntimeError("Engine not initialized")

        query_text = payload.get("query")
        if not query_text:
            raise ValueError("'query' is required")
        mode = (payload.get("mode") or "hybrid").lower()
        top_k = int(payload.get("topK") or payload.get("top_k") or 10)
        only_context = bool(payload.get("onlyNeedContext") or payload.get("only_need_context"))
        document_id_prefix = payload.get("documentIdPrefix") or payload.get("document_id_prefix")

        matched_ids: list[str] | None = None
        if document_id_prefix:
            matched_ids = [
                doc_id for doc_id in self._ingested if doc_id.startswith(document_id_prefix)
            ]
            if not matched_ids:
                return {
                    "query": query_text,
                    "mode": mode,
                    "topK": top_k,
                    "answer": "",
                    "matchedDocumentIds": [],
                }

        param_kwargs: dict = {"mode": mode, "top_k": top_k, "only_need_context": only_context}
        if matched_ids:
            param_kwargs["ids"] = matched_ids

        try:
            param = QueryParam(**param_kwargs)
        except TypeError:
            # LightRAG version doesn't expose ids on QueryParam — fall back to unfiltered query.
            logger.warning("QueryParam does not support 'ids' filter; running unfiltered query")
            param_kwargs.pop("ids", None)
            param = QueryParam(**param_kwargs)

        answer = await self.rag.aquery(query_text, param=param)
        return {
            "query": query_text,
            "mode": mode,
            "topK": top_k,
            "answer": answer,
            "matchedDocumentIds": matched_ids,
        }

    async def close(self) -> None:
        if self.rag is not None:
            try:
                await self.rag.finalize_storages()
            except Exception:
                logger.exception("Error finalizing LightRAG storages")
        if self.visual_store is not None:
            try:
                await self.visual_store.close()
            except Exception:
                logger.exception("Error closing visual store")
