"""Multimodal embedder client — implements `MultimodalEmbedderPort`.

Wraps OpenRouter's /v1/embeddings endpoint for models that accept the nested
content-array form (text + image_url parts). Confirmed working with
nvidia/llama-nemotron-embed-vl-1b-v2:free and Gemini Embedding 2 Preview.

Logic preserved from the original `service/multimodal_embedder.py` — just
moved into the infrastructure layer + namespaced.
"""
from __future__ import annotations

import asyncio
import json as _json
import logging
from typing import Any

import aiohttp

logger = logging.getLogger("rag-service.multimodal-embedder")


class OpenRouterMultimodalEmbedder:
    """Async client. Implements `MultimodalEmbedderPort`."""

    def __init__(
        self,
        *,
        base_url: str,
        api_key: str,
        model: str,
        max_concurrency: int = 2,
        request_timeout: float = 60.0,
    ) -> None:
        self.base_url = base_url.rstrip("/")
        self.api_key = api_key
        self.model = model
        self.timeout = request_timeout
        self._sem = asyncio.Semaphore(max_concurrency)
        self._dim: int | None = None

    @property
    def embedding_dim(self) -> int | None:
        return self._dim

    async def embed_text(self, text: str) -> list[float]:
        item = {"content": [{"type": "text", "text": text}]}
        embeddings = await self._embed([item])
        return embeddings[0]

    async def embed_image(
        self, image_url: str, caption: str | None = None
    ) -> list[float]:
        # First try with the URL — provider fetches it. Cheaper if it works.
        try:
            return await self._embed_image_with_url(image_url, caption)
        except Exception as ex:
            err_text = str(ex)
            permanent_markers = (
                "URL_ROBOTED",
                "ROBOTED_DENIED",
                "Provided image is not valid",
            )
            if any(m in err_text for m in permanent_markers):
                logger.warning(
                    "URL-mode image embed permanently rejected (%s); skipping data-URL fallback",
                    err_text[:160],
                )
                raise

            logger.warning(
                "URL-mode image embed failed (%s); falling back to base64 data URL",
                err_text[:160],
            )
            return await self._embed_image_via_data_url(image_url, caption)

    async def _embed_image_with_url(
        self, image_url: str, caption: str | None
    ) -> list[float]:
        parts: list[dict[str, Any]] = [{"type": "image_url", "image_url": {"url": image_url}}]
        if caption:
            parts.append({"type": "text", "text": caption})
        embeddings = await self._embed([{"content": parts}])
        return embeddings[0]

    async def _embed_image_via_data_url(
        self, image_url: str, caption: str | None
    ) -> list[float]:
        import base64
        async with aiohttp.ClientSession(
            timeout=aiohttp.ClientTimeout(total=20.0),
            headers={"User-Agent": "Mozilla/5.0 (compatible; MeAIRag/1.0)"},
        ) as session:
            async with session.get(image_url) as resp:
                if resp.status != 200:
                    raise RuntimeError(
                        f"Image download failed: HTTP {resp.status} for {image_url[:120]}"
                    )
                content_type = resp.headers.get("Content-Type", "image/jpeg")
                if not content_type.startswith("image/"):
                    raise RuntimeError(
                        f"Image download returned non-image content-type {content_type!r}"
                    )
                data = await resp.read()
        b64 = base64.b64encode(data).decode("ascii")
        data_url = f"data:{content_type};base64,{b64}"
        parts: list[dict[str, Any]] = [{"type": "image_url", "image_url": {"url": data_url}}]
        if caption:
            parts.append({"type": "text", "text": caption})
        embeddings = await self._embed([{"content": parts}])
        return embeddings[0]

    async def embed_batch_text(self, texts: list[str]) -> list[list[float]]:
        items = [{"content": [{"type": "text", "text": t}]} for t in texts]
        return await self._embed(items)

    async def _embed(self, items: list[dict]) -> list[list[float]]:
        body = {
            "model": self.model,
            "input": items,
            "encoding_format": "float",
        }
        headers = {
            "Authorization": f"Bearer {self.api_key}",
            "Content-Type": "application/json",
        }
        max_attempts = 4
        backoff_seconds = [1.0, 2.5, 6.0]

        async with self._sem:
            for attempt in range(max_attempts):
                try:
                    async with aiohttp.ClientSession(
                        timeout=aiohttp.ClientTimeout(total=self.timeout),
                    ) as session:
                        async with session.post(
                            f"{self.base_url}/embeddings",
                            headers=headers,
                            json=body,
                        ) as resp:
                            text = await resp.text()
                            if resp.status != 200:
                                raise RuntimeError(
                                    f"HTTP {resp.status} body={text[:400]}"
                                )
                            data = _json.loads(text)

                    rows = data.get("data") or []
                    if not rows:
                        raise RuntimeError(
                            f"Embedding response had no data: {data}"
                        )
                    embeddings = [row["embedding"] for row in rows]
                    if self._dim is None:
                        self._dim = len(embeddings[0])
                        logger.info(
                            "Multimodal embedder ready (model=%s dim=%d)",
                            self.model, self._dim,
                        )
                    return embeddings
                except Exception as ex:
                    err_text = str(ex)
                    permanent_markers = (
                        "URL_ROBOTED",
                        "ROBOTED_DENIED",
                        "Provided image is not valid",
                        "INVALID_ARGUMENT",
                    )
                    if any(m in err_text for m in permanent_markers):
                        logger.warning(
                            "Multimodal embed permanent failure (no retry): %s",
                            err_text[:240],
                        )
                        raise

                    if attempt + 1 >= max_attempts:
                        logger.error(
                            "Multimodal embed gave up after %d attempts: %s",
                            attempt + 1, err_text[:240],
                        )
                        raise
                    delay = backoff_seconds[attempt]
                    logger.warning(
                        "Multimodal embed attempt %d failed (%s); retrying in %.1fs",
                        attempt + 1, err_text[:240], delay,
                    )
                    await asyncio.sleep(delay)

        raise RuntimeError("unreachable")
