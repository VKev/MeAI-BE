"""Tiny FastAPI app that exposes `/health`. Used by Docker healthcheck +
external monitoring."""
from __future__ import annotations

from fastapi import FastAPI


def build_app() -> FastAPI:
    app = FastAPI(title="rag-microservice", docs_url=None, redoc_url=None)

    @app.get("/health")
    async def health() -> dict[str, str]:
        return {"status": "ok"}

    return app
