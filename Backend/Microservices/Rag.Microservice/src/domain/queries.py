"""Query-side domain types — what we ask, and what comes back."""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any


@dataclass(slots=True)
class TextQueryPayload:
    """LightRAG text query — single mode, optional document-id-prefix scoping."""
    query: str
    mode: str = "hybrid"                 # "naive" | "local" | "global" | "hybrid"
    top_k: int = 10
    only_need_context: bool = False
    document_id_prefix: str | None = None


@dataclass(slots=True)
class MultimodalQueryPayload:
    """Hybrid retrieval over text + visual + video legs."""
    query: str
    modes: tuple[str, ...] = ("text", "visual")
    top_k: int = 8
    document_id_prefix: str | None = None
    platform: str | None = None
    social_media_id: str | None = None


# ── Result shapes ──────────────────────────────────────────────────────────


@dataclass(slots=True)
class TextRetrievalResult:
    context: str
    matched_document_ids: list[str]


@dataclass(slots=True)
class VisualHit:
    document_id: str | None
    kind: str | None
    scope: str | None
    image_url: str | None
    mirrored_image_url: str | None
    caption: str | None
    post_id: str | None
    fingerprint: str | None
    score: float


@dataclass(slots=True)
class VideoSegmentHit:
    """One scored video segment after frame-level Qdrant query is collapsed
    to one hit per (video_name, segment_index). `frame_url` carries the URL
    of the highest-scoring frame within the segment."""
    video_name: str | None
    post_id: str | None
    index: str | None
    time: str | None
    caption: str | None
    transcript: str | None
    score: float
    frame_url: str | None = None
    frame_index: int | None = None
    scope: str | None = None


@dataclass(slots=True)
class MultimodalQueryResult:
    query: str
    top_k: int
    document_id_prefix: str
    text: TextRetrievalResult | None = None
    visual: list[VisualHit] = field(default_factory=list)
    video: list[VideoSegmentHit] = field(default_factory=list)
    visual_error: str | None = None
    video_error: str | None = None


@dataclass(slots=True)
class TextQueryResult:
    query: str
    mode: str
    top_k: int
    answer: str
    matched_document_ids: list[str] | None = None


@dataclass(slots=True)
class FingerprintListResult:
    fingerprints: dict[str, str]
    count: int
