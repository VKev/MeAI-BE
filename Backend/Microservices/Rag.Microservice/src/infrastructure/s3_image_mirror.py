"""S3 image mirror — implements `ImageMirrorPort`.

Mirrors external image URLs to our S3 bucket so Vertex AI / OpenAI image
fetchers can read them (Facebook CDN refuses both via robots.txt).

Uploads at ingest time, presigns at query time. We store the S3 KEY (not URL)
in payloads so 7-day URL expiry never matters.

Functions preserved from `service/image_mirror.py`; wrapped in a class that
implements the Port for clean-arch dependency inversion.
"""
from __future__ import annotations

import logging
import threading
from urllib.parse import urlparse, urlunparse

import aiohttp
import boto3
from botocore.config import Config

logger = logging.getLogger("rag-service.s3-image-mirror")


class S3ImageMirror:
    """Implements `ImageMirrorPort`."""

    def __init__(
        self,
        *,
        bucket: str,
        region: str = "ap-southeast-1",
        key_prefix: str = "local-vinhdo/videorag-frames/images/",
        ttl_seconds: int = 604800,
        public_base_url: str | None = "https://static.vkev.me",
    ) -> None:
        self._bucket = bucket
        self._region = region
        self._key_prefix = key_prefix.rstrip("/") + "/"
        self._ttl = ttl_seconds
        self._public_base_url = (public_base_url or "").rstrip("/")
        self._client = None
        self._lock = threading.Lock()

    def _get_client(self):
        if self._client is None:
            with self._lock:
                if self._client is None:
                    self._client = boto3.client(
                        "s3",
                        region_name=self._region,
                        endpoint_url=f"https://s3.{self._region}.amazonaws.com",
                        config=Config(
                            signature_version="s3v4",
                            retries={"max_attempts": 3, "mode": "standard"},
                        ),
                    )
        return self._client

    @staticmethod
    def _ext_from_content_type(content_type: str) -> str:
        ct = (content_type or "").lower()
        if "png" in ct: return "png"
        if "webp" in ct: return "webp"
        if "gif" in ct: return "gif"
        return "jpg"

    @staticmethod
    def _safe(part: str) -> str:
        return "".join(c if c.isalnum() or c in "-_" else "_" for c in part)

    async def upload(
        self, image_url: str, *, scope_hash: str, doc_id: str
    ) -> str | None:
        """Returns the S3 key on success, None on any failure."""
        if not image_url:
            return None

        timeout = aiohttp.ClientTimeout(total=20.0)
        headers = {"User-Agent": "Mozilla/5.0 (compatible; MeAIRag/1.0)"}
        try:
            async with aiohttp.ClientSession(timeout=timeout, headers=headers) as session:
                async with session.get(image_url) as resp:
                    if resp.status != 200:
                        logger.warning(
                            "mirror: source HTTP %d for %s",
                            resp.status, image_url[:160],
                        )
                        return None
                    content_type = resp.headers.get("Content-Type", "image/jpeg")
                    if not content_type.startswith("image/"):
                        logger.warning(
                            "mirror: non-image content-type %r for %s",
                            content_type, image_url[:160],
                        )
                        return None
                    data = await resp.read()
        except Exception:
            logger.warning("mirror: download failed for %s", image_url[:160], exc_info=True)
            return None

        ext = self._ext_from_content_type(content_type)
        key = f"{self._key_prefix}{self._safe(scope_hash)}/{self._safe(doc_id)}.{ext}"

        try:
            self._get_client().put_object(
                Bucket=self._bucket,
                Key=key,
                Body=data,
                ContentType=content_type,
            )
            logger.info("mirror: uploaded %d bytes → key=%s", len(data), key)
            return key
        except Exception:
            logger.warning("mirror: S3 upload failed for %s", image_url[:160], exc_info=True)
            return None

    def presign(self, key: str | None) -> str | None:
        """Generate a fresh presigned GET URL. Cheap (local crypto)."""
        if not key:
            return None
        try:
            url = self._get_client().generate_presigned_url(
                "get_object",
                Params={"Bucket": self._bucket, "Key": key},
                ExpiresIn=self._ttl,
            )
            return self._to_public_url(url)
        except Exception:
            logger.warning("presign failed for key=%s", key, exc_info=True)
            return None

    def _to_public_url(self, signed_url: str) -> str:
        """Rewrite path-style S3 presigned URLs through the public CDN host."""
        if not self._public_base_url:
            return signed_url

        source = urlparse(signed_url)
        public_base = urlparse(self._public_base_url)
        if not public_base.scheme or not public_base.netloc:
            logger.warning("invalid S3 public base URL %r; using direct S3 URL", self._public_base_url)
            return signed_url

        return urlunparse((
            public_base.scheme,
            public_base.netloc,
            source.path,
            "",
            source.query,
            "",
        ))
