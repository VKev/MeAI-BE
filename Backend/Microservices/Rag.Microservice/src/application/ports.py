"""Port interfaces (Protocols) — the boundary between the application layer
and infrastructure adapters.

Services in `src.application.services` depend ONLY on these Protocols. Concrete
implementations live in `src.infrastructure`. Composition root in
`src.composition.container` wires implementations into services at startup.

We use `typing.Protocol` (structural typing) instead of ABC because:
  - No subclass declaration boilerplate at the implementation site
  - Implementations remain framework-agnostic (don't have to inherit anything)
  - Easier to swap or fake in tests
"""
from __future__ import annotations

from typing import Any, Iterable, Protocol, runtime_checkable


@runtime_checkable
class FingerprintRegistry(Protocol):
    """Tracks which document_ids have been ingested and at what fingerprint.

    Used by `IngestService` to decide whether an incoming ingest is a no-op
    (skip), a fresh insert (new doc), or a replacement (fingerprint changed).
    Persisted to disk so restarts don't re-ingest the world.
    """

    async def reconcile(
        self, document_id: str | None, fingerprint: str | None
    ) -> str:
        """Returns one of "skipped" / "ingested" / "updated".

        - "skipped": existing fingerprint matches → caller must NOT re-insert
        - "ingested": doc not seen before → caller should insert
        - "updated": fingerprint differs → caller should DELETE then INSERT
        """
        ...

    async def record(
        self, document_id: str | None, fingerprint: str | None
    ) -> None:
        """Persist the (doc_id → fingerprint) pair after a successful ingest."""
        ...

    def list_with_prefix(self, prefix: str) -> dict[str, str]:
        """Snapshot of all currently-registered (doc_id → fingerprint) pairs
        whose doc_id startswith `prefix`. Pass empty string for all."""
        ...

    def matching_ids(self, prefix: str) -> list[str]:
        """Just the doc_ids whose prefix matches — used to scope LightRAG queries."""
        ...


@runtime_checkable
class LightRagFacade(Protocol):
    """Facade over LightRAG (text-mode RAG with entity/relationship graph).

    We don't expose LightRAG's full API — only the operations our services need.
    This keeps swap-in of an alternative text-mode RAG engine cheap.
    """

    async def insert_text(
        self,
        content: str | list[str],
        *,
        document_id: str | None = None,
        file_path: str | None = None,
    ) -> None: ...

    async def delete_by_document_id(self, document_id: str) -> None: ...

    async def query(
        self,
        query: str,
        *,
        mode: str = "hybrid",
        top_k: int = 10,
        only_need_context: bool = False,
        ids: list[str] | None = None,
    ) -> str: ...


@runtime_checkable
class MultimodalEmbedderPort(Protocol):
    """Multimodal (image + text) embedder used by the visual + video paths."""

    async def embed_text(self, text: str) -> list[float]: ...

    async def embed_image(
        self, image_url: str, caption: str | None = None
    ) -> list[float]: ...


@runtime_checkable
class VisualStorePort(Protocol):
    """Qdrant-backed vector store for image_native (image + caption) vectors."""

    async def upsert_point(
        self,
        document_id: str,
        kind: str,
        vector: list[float],
        scope: str,
        payload: dict[str, Any],
    ) -> None: ...

    async def delete_by_document_id(self, document_id: str) -> None: ...

    async def search(
        self,
        vector: list[float],
        top_k: int,
        scope: str | None = None,
    ) -> list[dict[str, Any]]: ...

    async def close(self) -> None: ...


@runtime_checkable
class ImageMirrorPort(Protocol):
    """Mirrors external image URLs to S3 to bypass robots.txt / CDN blocks
    on Vertex AI / OpenAI image-fetch paths."""

    async def upload(
        self, image_url: str, *, scope_hash: str, doc_id: str
    ) -> str | None:
        """Returns the S3 key on success, None on any failure (caller should
        fall back to the original URL)."""
        ...

    def presign(self, key: str | None) -> str | None:
        """Generate a fresh presigned URL for a previously-uploaded key.
        Cheap (local crypto) — safe to call on every retrieval hit."""
        ...


@runtime_checkable
class VisionDescriberPort(Protocol):
    """Vision-LLM that captions an image into text — used by the
    describe-then-text image ingest path (`kind=image`)."""

    async def describe(
        self, image_ref: str, custom_prompt: str | None = None
    ) -> str: ...


@runtime_checkable
class VideoRagEnginePort(Protocol):
    """VideoRAG facade. Per-account, per-platform isolation handled internally
    via `scope_for(...)`. Wraps the heavily-customized vendored videorag library."""

    async def ingest_video(self, payload: dict[str, Any]) -> dict[str, Any]: ...

    async def query_video(
        self,
        query: str,
        *,
        platform: str,
        social_media_id: str,
        top_k: int = 4,
    ) -> list[dict[str, Any]]: ...

    async def hydrate_segments(
        self, scope: str, hits: list[dict[str, Any]]
    ) -> list[dict[str, Any]]: ...

    def scope_for(self, platform: str, social_media_id: str) -> str: ...

    async def close(self) -> None: ...


@runtime_checkable
class KnowledgeLoaderPort(Protocol):
    """Reads markdown knowledge files from disk and yields parsed sections.

    Used by `KnowledgeBootstrapService` at startup. Abstracted so the source
    can be swapped (filesystem now, S3 / Git later) without touching the service.
    """

    def list_namespaces(self) -> Iterable[tuple[str, str]]:
        """Yields `(namespace, file_path)` for each `.md` file present."""
        ...

    def parse_namespace(self, namespace: str, file_path: str) -> list[Any]:
        """Parses the file at `file_path` into knowledge docs.

        Each item carries `document_id`, `content`, `fingerprint`. We return
        `Any` here because `KnowledgeDoc` lives in this same package and we'd
        otherwise need a circular import — concrete impls return KnowledgeDoc.
        """
        ...
