"""Document-side domain types — what we ingest, and what comes back."""
from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from typing import Any


class DocumentKind(str, Enum):
    """Kind discriminator for incoming ingest payloads.

    Using `str` Enum so values round-trip cleanly through JSON / gRPC without
    extra serialization adapters.
    """
    TEXT = "text"
    TEXTS = "texts"          # batched text — used by a few callers, kept for compat
    IMAGE = "image"          # describe-then-text path
    IMAGE_NATIVE = "image_native"  # multimodal pixel embedding
    VIDEO = "video"          # frame-level VideoRAG path


class IngestStatus(str, Enum):
    """Per-document ingest outcome. Reported back to the caller."""
    INGESTED = "ingested"
    UPDATED = "updated"
    SKIPPED = "skipped"      # fingerprint matched, no work performed
    FAILED = "failed"


@dataclass(slots=True)
class IngestPayload:
    """Normalized request shape for a single ingest. Built by the transport
    layer from whatever wire format (JSON / gRPC) the caller used."""

    kind: DocumentKind
    document_id: str | None = None
    fingerprint: str | None = None

    # Text path
    content: Any | None = None              # str for `text`, list[str] for `texts`

    # Image / image_native path
    image_url: str | None = None
    image_base64: str | None = None
    mime_type: str | None = None
    caption: str | None = None
    describe_prompt: str | None = None

    # image_native + video path
    scope: str | None = None
    post_id: str | None = None

    # Video path
    platform: str | None = None
    social_media_id: str | None = None
    video_url: str | None = None


@dataclass(slots=True)
class IngestResult:
    """Outcome envelope returned from an ingest call.

    `kind` echoes the caller's discriminator so batched callers can match each
    item to its result. `status` is the lifecycle state. `extra` carries any
    per-kind metadata (e.g. video segment count, image description) that the
    transport layer may serialize back to the caller.
    """
    document_id: str | None
    kind: DocumentKind
    status: IngestStatus
    fingerprint: str | None = None
    extra: dict[str, Any] = field(default_factory=dict)
    error: str | None = None
