"""Qdrant collection for multimodal (image + caption) vectors.

Lives alongside LightRAG's text collections in the same Qdrant instance.
Bypasses LightRAG entirely — image vectors don't fit its document/graph model.

Each post can produce up to two points:
  - vec:image    → embedding of the image (with caption fused if provided)
  - vec:caption  → embedding of the caption text in the same model space

Both share a `scope` payload field so we can filter by account at query time.
"""
from __future__ import annotations

import logging
import uuid
from typing import Any

from qdrant_client import AsyncQdrantClient
from qdrant_client.http import models as qm

logger = logging.getLogger("rag-service.qdrant-visual")

_NAMESPACE = uuid.UUID("0e2a3a88-2410-4e72-9ee9-cf9f5b3f0a01")


def _point_id(document_id: str, kind: str) -> str:
    return str(uuid.uuid5(_NAMESPACE, f"{document_id}|{kind}"))


class QdrantVisualStore:
    def __init__(
        self,
        url: str,
        api_key: str | None,
        collection: str,
    ) -> None:
        self.url = url
        self.api_key = api_key
        self.collection = collection
        self._client: AsyncQdrantClient | None = None
        self._dim: int | None = None

    async def initialize(self, dim: int) -> None:
        self._dim = dim
        self._client = AsyncQdrantClient(url=self.url, api_key=self.api_key)
        existing = await self._client.collection_exists(self.collection)
        if not existing:
            await self._client.create_collection(
                collection_name=self.collection,
                vectors_config=qm.VectorParams(size=dim, distance=qm.Distance.COSINE),
            )
            logger.info("Created Qdrant visual collection '%s' (dim=%d)", self.collection, dim)

            # Index the scope field so prefix queries are fast.
            await self._client.create_payload_index(
                collection_name=self.collection,
                field_name="scope",
                field_schema=qm.PayloadSchemaType.KEYWORD,
            )
            await self._client.create_payload_index(
                collection_name=self.collection,
                field_name="document_id",
                field_schema=qm.PayloadSchemaType.KEYWORD,
            )
        else:
            info = await self._client.get_collection(self.collection)
            current_dim = info.config.params.vectors.size
            if current_dim != dim:
                raise RuntimeError(
                    f"Visual collection '{self.collection}' has dim {current_dim} "
                    f"but embedder produces {dim}; recreate the collection or change the model."
                )
            logger.info(
                "Reusing Qdrant visual collection '%s' (dim=%d)", self.collection, current_dim
            )

    async def upsert_point(
        self,
        document_id: str,
        kind: str,
        vector: list[float],
        scope: str,
        payload: dict[str, Any],
    ) -> None:
        if self._client is None:
            raise RuntimeError("QdrantVisualStore not initialized")
        full_payload = {
            "document_id": document_id,
            "kind": kind,
            "scope": scope,
            **payload,
        }
        point = qm.PointStruct(
            id=_point_id(document_id, kind),
            vector=vector,
            payload=full_payload,
        )
        await self._client.upsert(collection_name=self.collection, points=[point])

    async def delete_by_document_id(self, document_id: str) -> None:
        if self._client is None:
            return
        ids = [_point_id(document_id, kind) for kind in ("image", "caption")]
        await self._client.delete(
            collection_name=self.collection,
            points_selector=qm.PointIdsList(points=ids),
        )

    async def search(
        self,
        vector: list[float],
        top_k: int,
        scope: str | None = None,
    ) -> list[dict[str, Any]]:
        if self._client is None:
            raise RuntimeError("QdrantVisualStore not initialized")
        flt: qm.Filter | None = None
        if scope:
            flt = qm.Filter(
                must=[qm.FieldCondition(key="scope", match=qm.MatchValue(value=scope))]
            )
        result = await self._client.query_points(
            collection_name=self.collection,
            query=vector,
            limit=top_k,
            query_filter=flt,
            with_payload=True,
        )
        hits: list[dict[str, Any]] = []
        for p in result.points:
            payload = p.payload or {}
            hits.append(
                {
                    "documentId": payload.get("document_id"),
                    "kind": payload.get("kind"),
                    "scope": payload.get("scope"),
                    "imageUrl": payload.get("image_url"),
                    "caption": payload.get("caption"),
                    "postId": payload.get("post_id"),
                    "fingerprint": payload.get("fingerprint"),
                    "score": p.score,
                }
            )
        return hits

    async def close(self) -> None:
        if self._client is not None:
            try:
                await self._client.close()
            except Exception:
                logger.exception("Error closing AsyncQdrantClient")
