"""Tiny FastAPI app for /healthz so docker can verify the process is alive."""
from __future__ import annotations

from fastapi import FastAPI


def build_app() -> FastAPI:
    app = FastAPI(title="MeAI Rag Microservice", version="0.1.0")

    @app.get("/healthz")
    async def healthz() -> dict:
        return {"status": "ok"}

    return app
