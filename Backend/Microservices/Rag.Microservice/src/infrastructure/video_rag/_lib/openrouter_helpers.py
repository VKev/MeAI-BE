"""Sync HTTP helpers for OpenRouter calls inside videorag's worker subprocesses.

videorag uses multiprocessing.Process(start_method='spawn') for some steps, so we
can't rely on async/aiohttp state established in the parent. These helpers use the
sync `requests` library — tiny, robust, fork/spawn-safe.

Configuration is env-driven so spawn'd subprocesses pick it up automatically.
"""
from __future__ import annotations

import base64
import io
import logging
import os
import time
from typing import Iterable

import requests

logger = logging.getLogger("videorag.openrouter")


def _env(key: str, default: str | None = None) -> str:
    val = os.environ.get(key, default)
    if val is None:
        raise RuntimeError(f"Required env var {key} is not set")
    return val


def base_url() -> str:
    return _env("MULTIMODAL_EMBED_BASE_URL", _env("LLM_BASE_URL")).rstrip("/")


def api_key() -> str:
    return _env("MULTIMODAL_EMBED_API_KEY", _env("LLM_API_KEY"))


def llm_model() -> str:
    return os.environ.get("VIDEORAG_LLM_MODEL", "openai/gpt-4o-mini")


def audio_model() -> str:
    return os.environ.get("VIDEORAG_AUDIO_MODEL", "openai/whisper-1")


def multimodal_embed_model() -> str:
    return os.environ.get(
        "MULTIMODAL_EMBED_MODEL", "google/gemini-embedding-2-preview"
    )


def request_timeout() -> int:
    return int(os.environ.get("VIDEORAG_HTTP_TIMEOUT", "120"))


# --- Whisper / transcription ------------------------------------------------


def transcribe_audio(audio_bytes: bytes, filename: str, content_type: str) -> str:
    """POST /audio/transcriptions to OpenRouter. Returns the transcript text.

    OpenRouter does NOT use the OpenAI-compatible multipart shape — it has its
    own JSON-with-base64 format, taking `input_audio: {data: <b64>, format: <ext>}`.
    Sending multipart returns a JSON-parse error like "No number after minus sign".

    `filename` is unused (kept for signature stability with the upstream caller);
    `content_type` is parsed to derive the audio format hint.
    """
    audio_format = content_type.split("/", 1)[1] if "/" in content_type else "mp3"
    if audio_format == "mpeg":
        audio_format = "mp3"

    body = {
        "model": audio_model(),
        "input_audio": {
            "data": base64.b64encode(audio_bytes).decode("ascii"),
            "format": audio_format,
        },
    }
    headers = {
        "Authorization": f"Bearer {api_key()}",
        "Content-Type": "application/json",
    }
    resp = requests.post(
        f"{base_url()}/audio/transcriptions",
        headers=headers,
        json=body,
        timeout=request_timeout(),
    )
    if resp.status_code != 200:
        raise RuntimeError(
            f"Whisper HTTP {resp.status_code}: {resp.text[:400]}"
        )
    payload = resp.json()
    return (payload.get("text") or payload.get("transcription") or "").strip()


# --- Multimodal chat / motion-aware caption ---------------------------------


def chat_completions(messages: list[dict], temperature: float = 0.4) -> str:
    """POST /chat/completions. Returns the assistant message text content."""
    url = f"{base_url()}/chat/completions"
    headers = {
        "Authorization": f"Bearer {api_key()}",
        "Content-Type": "application/json",
        "HTTP-Referer": os.environ.get("VIDEORAG_REFERER", "http://localhost:2406"),
        "X-Title": "MeAI VideoRAG",
    }
    body = {
        "model": llm_model(),
        "messages": messages,
        "temperature": temperature,
    }
    last_err: Exception | None = None
    for attempt in range(3):
        try:
            resp = requests.post(url, headers=headers, json=body, timeout=request_timeout())
            if resp.status_code != 200:
                raise RuntimeError(f"chat HTTP {resp.status_code}: {resp.text[:400]}")
            data = resp.json()
            return (data["choices"][0]["message"]["content"] or "").strip()
        except Exception as ex:
            last_err = ex
            logger.warning("chat_completions attempt %d failed: %s", attempt + 1, ex)
            time.sleep(1.0 * (attempt + 1))
    raise RuntimeError(f"chat_completions gave up: {last_err}")


# --- Multimodal embedding (B2: multiple frames + text) ----------------------


def multimodal_embedding(image_urls: Iterable[str], text: str | None = None) -> list[float]:
    """POST /embeddings with content-array input. Returns one float[3072] vector."""
    url = f"{base_url()}/embeddings"
    parts: list[dict] = []
    for u in image_urls:
        if u:
            parts.append({"type": "image_url", "image_url": {"url": u}})
    if text:
        parts.append({"type": "text", "text": text[:8000]})  # Gemini context limit
    if not parts:
        raise ValueError("multimodal_embedding requires at least one frame or text")

    headers = {
        "Authorization": f"Bearer {api_key()}",
        "Content-Type": "application/json",
    }
    body = {
        "model": multimodal_embed_model(),
        "input": [{"content": parts}],
        "encoding_format": "float",
    }
    last_err: Exception | None = None
    for attempt in range(3):
        try:
            resp = requests.post(url, headers=headers, json=body, timeout=request_timeout())
            if resp.status_code != 200:
                raise RuntimeError(f"embed HTTP {resp.status_code}: {resp.text[:400]}")
            data = resp.json()
            if not data.get("data"):
                raise RuntimeError(f"embed empty body: {data}")
            return data["data"][0]["embedding"]
        except Exception as ex:
            last_err = ex
            err_text = str(ex)
            # Permanent provider errors — bail without further retries
            if any(m in err_text for m in (
                "URL_ROBOTED", "ROBOTED_DENIED",
                "Provided image is not valid", "INVALID_ARGUMENT",
            )):
                logger.warning("multimodal_embedding permanent failure (no retry): %s",
                               err_text[:240])
                raise
            logger.warning("multimodal_embedding attempt %d failed: %s", attempt + 1, ex)
            time.sleep(1.0 * (attempt + 1))
    raise RuntimeError(f"multimodal_embedding gave up: {last_err}")


def multimodal_embedding_batch(
    inputs: list[list[dict]],
) -> list[list[float]]:
    """Batched multimodal embedding — N input "documents" → N vectors in one HTTP call.

    Each element of `inputs` is a list of content parts (image_url and/or text)
    matching the same shape as one call's `parts` would have in
    :func:`multimodal_embedding`. Used by the per-frame encoder so we can embed
    all 5 frames of a segment in a single API round-trip instead of 5 sequential
    calls.

    Returns a list of float[3072] vectors in the same order as `inputs`.
    """
    if not inputs:
        return []
    url = f"{base_url()}/embeddings"
    body_inputs = [{"content": parts} for parts in inputs if parts]
    if not body_inputs:
        return []
    headers = {
        "Authorization": f"Bearer {api_key()}",
        "Content-Type": "application/json",
    }
    body = {
        "model": multimodal_embed_model(),
        "input": body_inputs,
        "encoding_format": "float",
    }
    last_err: Exception | None = None
    for attempt in range(3):
        try:
            resp = requests.post(url, headers=headers, json=body, timeout=request_timeout())
            if resp.status_code != 200:
                raise RuntimeError(f"embed batch HTTP {resp.status_code}: {resp.text[:400]}")
            data = resp.json()
            items = data.get("data") or []
            if len(items) != len(body_inputs):
                raise RuntimeError(
                    f"embed batch mismatch: sent {len(body_inputs)} got {len(items)}"
                )
            # Provider may return out-of-order items; sort by `index` if present.
            items.sort(key=lambda x: int(x.get("index") or 0))
            return [item["embedding"] for item in items]
        except Exception as ex:
            last_err = ex
            err_text = str(ex)
            if any(m in err_text for m in (
                "URL_ROBOTED", "ROBOTED_DENIED",
                "Provided image is not valid", "INVALID_ARGUMENT",
            )):
                logger.warning("multimodal_embedding_batch permanent failure (no retry): %s",
                               err_text[:240])
                raise
            logger.warning("multimodal_embedding_batch attempt %d failed: %s", attempt + 1, ex)
            time.sleep(1.0 * (attempt + 1))
    raise RuntimeError(f"multimodal_embedding_batch gave up: {last_err}")


def text_embedding(text: str) -> list[float]:
    """Text-only multimodal embed — same model, same vector space as image embeds."""
    return multimodal_embedding(image_urls=[], text=text)


def openai_text_embedding(text: str) -> list[float]:
    """Sync 1536-dim text embedding via the OpenAI-compatible /embeddings endpoint.

    Uses EMBED_MODEL (defaults to openai/text-embedding-3-small) — same model the
    upstream VideoRAG ingestion path uses for the per-scope chunks_vdb. Used by
    the hybrid video query flow's text leg, which has to embed the user query
    into the SAME 1536-d space as the chunks_vdb's stored vectors.

    Distinct from `text_embedding()` which produces a 3072-d Gemini Embed 2 vector
    (incompatible with the chunks_vdb).
    """
    embed_base = os.environ.get("EMBED_BASE_URL", base_url()).rstrip("/")
    url_ = f"{embed_base}/embeddings"
    api = os.environ.get("EMBED_API_KEY") or api_key()
    model = os.environ.get("EMBED_MODEL", "openai/text-embedding-3-small")
    headers = {
        "Authorization": f"Bearer {api}",
        "Content-Type": "application/json",
    }
    body = {
        "model": model,
        "input": [text or ""],
        "encoding_format": "float",
    }
    last_err: Exception | None = None
    for attempt in range(3):
        try:
            resp = requests.post(url_, headers=headers, json=body, timeout=request_timeout())
            if resp.status_code != 200:
                raise RuntimeError(f"openai-embed HTTP {resp.status_code}: {resp.text[:400]}")
            data = resp.json()
            if not data.get("data"):
                raise RuntimeError(f"openai-embed empty body: {data}")
            return data["data"][0]["embedding"]
        except Exception as ex:
            last_err = ex
            logger.warning("openai_text_embedding attempt %d failed: %s", attempt + 1, ex)
            time.sleep(1.0 * (attempt + 1))
    raise RuntimeError(f"openai_text_embedding gave up: {last_err}")


# --- Helpers to encode in-memory PIL images / numpy frames to bytes ---------


def encode_pil_to_jpeg_bytes(pil_image, quality: int = 85) -> bytes:
    buf = io.BytesIO()
    pil_image.convert("RGB").save(buf, format="JPEG", quality=quality)
    return buf.getvalue()
