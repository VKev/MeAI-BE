"""Sync S3 frame uploader for videorag.

Uploads sampled video frames (PIL images or JPEG bytes) to S3 under a per-scope
key prefix and returns presigned URLs. The URLs are what we hand to Gemini
Embedding 2 / gpt-4o-mini multimodal — Vertex respects robots.txt and refuses
data: URLs, so frames must live at a public-fetchable URL.

Env-driven so spawn'd subprocesses pick it up at import time.
"""
from __future__ import annotations

import logging
import os
import threading
from typing import Iterable
from urllib.parse import urlparse, urlunparse

import boto3
from botocore.config import Config

logger = logging.getLogger("videorag.s3-frames")

_CLIENT_LOCK = threading.Lock()
_CLIENT = None


def _client():
    global _CLIENT
    if _CLIENT is None:
        with _CLIENT_LOCK:
            if _CLIENT is None:
                region = os.environ.get("VIDEORAG_S3_REGION", "ap-southeast-1")
                _CLIENT = boto3.client(
                    "s3",
                    region_name=region,
                    endpoint_url=f"https://s3.{region}.amazonaws.com",
                    config=Config(
                        signature_version="s3v4",
                        retries={"max_attempts": 3, "mode": "standard"},
                    ),
                )
    return _CLIENT


def _bucket() -> str:
    val = os.environ.get("VIDEORAG_S3_BUCKET")
    if not val:
        raise RuntimeError("VIDEORAG_S3_BUCKET is not set")
    return val


def _key_prefix() -> str:
    return os.environ.get("VIDEORAG_S3_KEY_PREFIX", "local-vinh/videorag-frames/").rstrip("/") + "/"


def _ttl_seconds() -> int:
    return int(os.environ.get("VIDEORAG_S3_FRAME_TTL_SECONDS", "604800"))  # 7 days


def _public_base_url() -> str:
    return (
        os.environ.get("VIDEORAG_S3_PUBLIC_BASE_URL")
        or os.environ.get("S3__PublicBaseUrl")
        or "https://static.vkev.me"
    ).rstrip("/")


def _to_public_url(signed_url: str) -> str:
    public_base = _public_base_url()
    if not public_base:
        return signed_url

    source = urlparse(signed_url)
    public = urlparse(public_base)
    if not public.scheme or not public.netloc:
        logger.warning("invalid S3 public base URL %r; using direct S3 URL", public_base)
        return signed_url

    return urlunparse((
        public.scheme,
        public.netloc,
        source.path,
        "",
        source.query,
        "",
    ))


def upload_frame_jpeg(jpeg_bytes: bytes, *, scope_hash: str, post_id: str,
                      segment_id: str, frame_index: int) -> str:
    """Uploads one JPEG to S3 and returns a presigned GET URL.

    Key layout: <prefix>/<scope_hash>/<post_id>/<segment_id>_<frame_index>.jpg
    """
    key = f"{_key_prefix()}{scope_hash}/{post_id}/{segment_id}_{frame_index}.jpg"
    _client().put_object(
        Bucket=_bucket(),
        Key=key,
        Body=jpeg_bytes,
        ContentType="image/jpeg",
    )
    url = _client().generate_presigned_url(
        "get_object",
        Params={"Bucket": _bucket(), "Key": key},
        ExpiresIn=_ttl_seconds(),
    )
    return _to_public_url(url)


def upload_pil_frames(pil_frames: Iterable, *, scope_hash: str, post_id: str,
                     segment_id: str) -> list[str]:
    """Convenience: takes PIL.Image instances, encodes JPEG, uploads, returns URLs."""
    from .openrouter_helpers import encode_pil_to_jpeg_bytes
    urls: list[str] = []
    for i, frame in enumerate(pil_frames):
        try:
            jpg = encode_pil_to_jpeg_bytes(frame)
            url = upload_frame_jpeg(
                jpg,
                scope_hash=scope_hash,
                post_id=post_id,
                segment_id=segment_id,
                frame_index=i,
            )
            urls.append(url)
        except Exception as ex:
            logger.warning(
                "frame upload failed (scope=%s post=%s seg=%s i=%d): %s",
                scope_hash, post_id, segment_id, i, ex,
            )
    return urls
