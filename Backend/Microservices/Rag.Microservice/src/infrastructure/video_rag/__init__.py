"""Video RAG adapter — wraps the vendored `videorag/` library and presents
it through the `VideoRagEnginePort` Protocol.

The library itself lives at the repo root (`Rag.Microservice/videorag/`). It's
a customized fork of the upstream VideoRAG research repo and is treated as
external — internal restructuring is out of scope.

This package contains only:
  - `adapter.py`     — implements VideoRagEnginePort
  - `downloader.py`  — yt-dlp + requests video download helper
"""
from .adapter import VideoRagAdapter

__all__ = ["VideoRagAdapter"]
