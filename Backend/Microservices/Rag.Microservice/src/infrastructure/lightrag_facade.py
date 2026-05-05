"""LightRAG facade — concrete implementation of `LightRagFacade` Protocol.

Wraps the `lightrag-hku` package. We don't expose LightRAG's full API surface;
only the operations our services need (insert, delete-by-id, query). This keeps
swap-out cheap if we ever want to replace LightRAG with a different text-mode
RAG engine.

Initialization is two-phase to match LightRAG's contract:
  1. `__init__` — sync; build the LightRAG object
  2. `initialize_async` — must be awaited before first use; opens the storage
     handles and starts the background pipeline
"""
from __future__ import annotations

import contextvars
import logging
import os
from typing import Any

from lightrag import LightRAG, QueryParam
from lightrag.kg.shared_storage import initialize_pipeline_status
from lightrag.llm.openai import openai_complete_if_cache, openai_embed
from lightrag.rerank import jina_rerank
from lightrag.utils import EmbeddingFunc, setup_logger

logger = logging.getLogger("rag-service.lightrag-facade")

# Per-call allowlist of `full_doc_id` values for Qdrant chunks_vdb scoping.
# Set by `query()` before delegating to LightRAG, read by the patched
# QdrantVectorDBStorage.query (installed at module import). None = unfiltered.
_chunk_id_allowlist: contextvars.ContextVar[list[str] | None] = contextvars.ContextVar(
    "lightrag_facade_chunk_id_allowlist", default=None,
)


def _install_qdrant_filter_patch() -> None:
    """Monkeypatch `QdrantVectorDBStorage.query` once. The patched method adds
    an extra `MatchAny(full_doc_id ∈ allowlist)` Qdrant filter when the
    contextvar is set, on top of LightRAG's built-in `workspace_filter`.

    This is how we get per-account scoping for the chunks vector DB without
    having to ship per-account LightRAG instances. LightRAG's QueryParam
    dropped the `ids` filter API in newer versions, so this is the only
    plausible hook short of forking the library.

    Only the chunks namespace is filtered. Entities/relations are derived
    metadata over chunks; if a chunk isn't in scope, its entities/relations
    aren't surfaced to the recommendation context anyway.
    """
    if getattr(_install_qdrant_filter_patch, "_installed", False):
        return
    # `CREATED_AT_FIELD` is a module-local constant inside qdrant_impl, not
    # exported via lightrag.constants. Pull it from the same module so we
    # mirror the original return shape exactly.
    from lightrag.kg.qdrant_impl import (
        CREATED_AT_FIELD,
        QdrantVectorDBStorage,
        workspace_filter_condition,
    )
    from qdrant_client.models import FieldCondition, Filter, MatchAny

    original_query = QdrantVectorDBStorage.query

    async def patched_query(self, query, top_k, query_embedding=None):
        allowlist = _chunk_id_allowlist.get()
        if allowlist is None or "chunks" not in (self.namespace or ""):
            return await original_query(self, query, top_k, query_embedding)

        if query_embedding is not None:
            embedding = query_embedding
        else:
            embedding_result = await self.embedding_func([query], _priority=5)
            embedding = embedding_result[0]

        results = self._client.query_points(
            collection_name=self.final_namespace,
            query=embedding,
            limit=top_k,
            with_payload=True,
            score_threshold=self.cosine_better_than_threshold,
            query_filter=Filter(must=[
                workspace_filter_condition(self.effective_workspace),
                FieldCondition(key="full_doc_id", match=MatchAny(any=allowlist)),
            ]),
        ).points

        return [
            {
                **dp.payload,
                "distance": dp.score,
                CREATED_AT_FIELD: dp.payload.get(CREATED_AT_FIELD),
            }
            for dp in results
        ]

    QdrantVectorDBStorage.query = patched_query
    _install_qdrant_filter_patch._installed = True
    logger.info("Installed QdrantVectorDBStorage.query monkeypatch for per-call doc-id scoping")


# Apply the patch eagerly at import time so it's in place before LightRAG ever queries.
_install_qdrant_filter_patch()


class LightRagFacadeImpl:
    """Implements `LightRagFacade` Protocol."""

    def __init__(
        self,
        *,
        working_dir: str,
        log_level: str,
        llm_base_url: str,
        llm_api_key: str,
        llm_model: str,
        embed_base_url: str,
        embed_api_key: str,
        embed_model: str,
        embed_dim: int,
        embed_max_tokens: int,
        qdrant_url: str,
        qdrant_api_key: str | None,
        qdrant_namespace: str,
        rerank_api_key: str = "",
        rerank_base_url: str = "https://api.jina.ai/v1/rerank",
        rerank_model: str = "jina-reranker-v2-base-multilingual",
    ) -> None:
        # LightRAG's QdrantVectorDBStorage adapter reads these from env at
        # construction; mirror them here so cleanup of the env layer is safe.
        os.environ["QDRANT_URL"] = qdrant_url
        if qdrant_api_key:
            os.environ["QDRANT_API_KEY"] = qdrant_api_key

        setup_logger("lightrag", level=log_level)

        async def llm_func(
            prompt: str,
            system_prompt: str | None = None,
            history_messages: list[Any] | None = None,
            **kwargs: Any,
        ) -> str:
            return await openai_complete_if_cache(
                llm_model,
                prompt,
                system_prompt=system_prompt,
                history_messages=history_messages or [],
                api_key=llm_api_key,
                base_url=llm_base_url,
                **kwargs,
            )

        async def embed_func(texts: list[str]) -> Any:
            return await openai_embed(
                texts,
                model=embed_model,
                api_key=embed_api_key,
                base_url=embed_base_url,
            )

        embedding = EmbeddingFunc(
            embedding_dim=embed_dim,
            max_token_size=embed_max_tokens,
            func=embed_func,
        )

        # Wire Jina rerank into LightRAG's text-mode retrieval. LightRAG calls
        # this with (query, documents, top_n) and expects a list of
        # {"index": int, "relevance_score": float}. `jina_rerank` matches that
        # contract exactly. If no key is configured, leave it None — LightRAG
        # then skips reranking silently (no warning).
        rerank_func = None
        if rerank_api_key:
            async def rerank_func(query, documents, top_n=None, **_):  # noqa: E306
                return await jina_rerank(
                    query=query,
                    documents=documents,
                    top_n=top_n,
                    api_key=rerank_api_key,
                    model=rerank_model,
                    base_url=rerank_base_url,
                )

        rag_kwargs: dict[str, Any] = dict(
            working_dir=working_dir,
            llm_model_func=llm_func,
            embedding_func=embedding,
            vector_storage="QdrantVectorDBStorage",
            workspace=qdrant_namespace,
        )
        if rerank_func is not None:
            rag_kwargs["rerank_model_func"] = rerank_func

        self._rag = LightRAG(**rag_kwargs)

        self._llm_model = llm_model
        self._embed_model = embed_model
        self._qdrant_url = qdrant_url
        self._qdrant_namespace = qdrant_namespace
        self._rerank_enabled = rerank_func is not None
        self._rerank_model = rerank_model if rerank_func is not None else "(disabled)"

    async def initialize_async(self) -> None:
        """Must be awaited before any insert/query call."""
        await self._rag.initialize_storages()
        await initialize_pipeline_status()
        logger.info(
            "LightRAG ready (llm=%s embed=%s qdrant=%s namespace=%s rerank=%s)",
            self._llm_model, self._embed_model, self._qdrant_url,
            self._qdrant_namespace, self._rerank_model,
        )

    async def close(self) -> None:
        try:
            await self._rag.finalize_storages()
        except Exception:
            logger.exception("Error finalizing LightRAG storages")

    @property
    def raw(self) -> LightRAG:
        """Escape hatch for code paths that need the full LightRAG API.
        Use sparingly — every direct use here is a missed Protocol method."""
        return self._rag

    # ── LightRagFacade Protocol implementation ─────────────────────────────

    async def insert_text(
        self,
        content: str | list[str],
        *,
        document_id: str | None = None,
        file_path: str | None = None,
    ) -> None:
        if isinstance(content, list):
            await self._rag.ainsert(content)
            return
        await self._rag.ainsert(
            content,
            ids=document_id,
            file_paths=file_path or document_id or "text-input",
        )

    async def delete_by_document_id(self, document_id: str) -> None:
        await self._rag.adelete_by_doc_id(document_id)

    async def query(
        self,
        query: str,
        *,
        mode: str = "hybrid",
        top_k: int = 10,
        only_need_context: bool = False,
        ids: list[str] | None = None,
    ) -> str:
        param = QueryParam(
            mode=mode,
            top_k=top_k,
            only_need_context=only_need_context,
        )
        # Per-account scoping: set the chunk-id allowlist contextvar so the
        # patched QdrantVectorDBStorage.query (installed at module import)
        # filters chunks by `full_doc_id ∈ ids` on top of LightRAG's workspace
        # filter. Cleared in `finally` so concurrent unrelated queries aren't
        # affected.
        token = _chunk_id_allowlist.set(ids if ids else None)
        try:
            result = await self._rag.aquery(query, param=param)
        finally:
            _chunk_id_allowlist.reset(token)
        return result if isinstance(result, str) else str(result)
