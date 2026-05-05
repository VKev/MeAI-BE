"""Pure domain types — no I/O, no framework dependencies.

These shapes describe WHAT we ingest and WHAT we return, not HOW any of it
happens. Anything that depends on Qdrant, OpenAI, S3, etc. lives in
`src.infrastructure`. Anything that orchestrates them lives in
`src.application.services`.
"""
from .documents import (
    DocumentKind,
    IngestPayload,
    IngestStatus,
    IngestResult,
)
from .queries import (
    TextQueryPayload,
    MultimodalQueryPayload,
    TextRetrievalResult,
    VisualHit,
    VideoSegmentHit,
    MultimodalQueryResult,
    TextQueryResult,
    FingerprintListResult,
)

__all__ = [
    "DocumentKind",
    "IngestPayload",
    "IngestStatus",
    "IngestResult",
    "TextQueryPayload",
    "MultimodalQueryPayload",
    "TextRetrievalResult",
    "VisualHit",
    "VideoSegmentHit",
    "MultimodalQueryResult",
    "TextQueryResult",
    "FingerprintListResult",
]
