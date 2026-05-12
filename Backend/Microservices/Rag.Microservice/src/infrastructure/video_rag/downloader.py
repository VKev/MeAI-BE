"""Download Facebook/Instagram video URLs into a local mp4 with audio.

Why yt-dlp instead of `requests.get`:
  Meta serves reels (and most modern video posts) as DASH — video and audio are
  on separate URLs referenced by an .mpd manifest. yt-dlp parses the DASH
  manifest behind a public viewer URL and muxes both tracks via ffmpeg.

We fall back to `requests` for direct mp4 URLs (older non-reel videos +
S3-mirror fetches).
"""
from __future__ import annotations

import contextlib
import logging
import os
import shutil
import tempfile
from typing import Iterator
from urllib.parse import urlparse

import requests

logger = logging.getLogger("rag-service.videorag-downloader")


class VideoDownloadUnavailableError(RuntimeError):
    """Raised when a social video page cannot be converted into local video bytes."""


def _http_timeout() -> int:
    return int(os.environ.get("VIDEORAG_HTTP_TIMEOUT", "120"))


def _download_headers() -> dict[str, str]:
    return {
        "User-Agent": (
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 "
            "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        ),
        "Accept": "video/mp4,video/*;q=0.9,*/*;q=0.8",
    }


def _looks_like_viewer_page(url: str) -> bool:
    try:
        host = urlparse(url).hostname or ""
    except Exception:
        return False
    return host.endswith("facebook.com") or host.endswith("instagram.com")


def _download_via_yt_dlp(url: str, out_dir: str, base_name: str) -> str:
    import yt_dlp

    out_template = os.path.join(out_dir, f"{base_name}.%(ext)s")
    opts = {
        "format": "bestvideo+bestaudio/best",
        "merge_output_format": "mp4",
        "outtmpl": out_template,
        "quiet": True,
        "no_warnings": True,
        "noprogress": True,
        "writethumbnail": False,
        "writesubtitles": False,
        "writeinfojson": False,
        "concurrent_fragment_downloads": 4,
        "socket_timeout": _http_timeout(),
        "http_headers": _download_headers(),
    }
    try:
        with yt_dlp.YoutubeDL(opts) as ydl:
            ydl.download([url])
    except yt_dlp.utils.DownloadError as ex:
        raw_message = str(ex)
        if "Cannot parse data" in raw_message and "facebook" in raw_message.lower():
            logger.warning("yt-dlp could not parse Facebook video page: %s", raw_message[:300])
            raise VideoDownloadUnavailableError(
                "Facebook video could not be indexed by VideoRAG because the viewer page could not be parsed. "
                "This is usually caused by a private, expired, restricted, or unsupported Facebook video page. "
                "AI can still use the post caption and image context that indexed successfully."
            ) from ex

        logger.warning("yt-dlp video download failed: %s", raw_message[:300])
        raise VideoDownloadUnavailableError(
            "Video could not be indexed by VideoRAG because the source video could not be downloaded. "
            "AI can still use any text or image context that indexed successfully."
        ) from ex

    candidates = [
        os.path.join(out_dir, f"{base_name}.mp4"),
        os.path.join(out_dir, f"{base_name}.mkv"),
    ]
    for cand in candidates:
        if os.path.exists(cand):
            return cand

    leftovers = [
        os.path.join(out_dir, f) for f in os.listdir(out_dir)
        if f.startswith(base_name + ".")
    ]
    if leftovers:
        return leftovers[0]
    raise RuntimeError(f"yt-dlp completed but no output file found in {out_dir}")


def _download_via_requests(url: str, out_path: str) -> None:
    with requests.get(
        url,
        stream=True,
        timeout=_http_timeout(),
        headers=_download_headers(),
    ) as resp:
        if resp.status_code != 200:
            raise RuntimeError(
                f"video download HTTP {resp.status_code} for {url}: "
                f"{resp.text[:200]}"
            )
        with open(out_path, "wb") as f:
            for chunk in resp.iter_content(chunk_size=1024 * 256):
                if chunk:
                    f.write(chunk)


@contextlib.contextmanager
def fetch_video_to_temp(
    url: str, *, scope_hash: str, post_id: str, ext: str = "mp4"
) -> Iterator[str]:
    """Yields a local path containing video+audio bytes for `url`.

    Basename embeds `<scope_hash>__<post_id>` so the vendored caption / feature
    encoders can derive scope + post_id from it.
    """
    safe_post_id = "".join(c if c.isalnum() or c in "-_" else "_" for c in post_id)
    safe_scope = "".join(c if c.isalnum() else "_" for c in scope_hash)
    base_name = f"{safe_scope}__{safe_post_id}"

    tmp_dir = tempfile.mkdtemp(prefix="videorag_dl_")
    final_path: str | None = None
    try:
        if _looks_like_viewer_page(url):
            logger.info("yt-dlp downloading viewer URL for scope=%s post=%s", scope_hash, post_id)
            final_path = _download_via_yt_dlp(url, tmp_dir, base_name)
        else:
            final_path = os.path.join(tmp_dir, f"{base_name}.{ext}")
            _download_via_requests(url, final_path)

        size = os.path.getsize(final_path)
        logger.info("Downloaded %d bytes (%s) for scope=%s post=%s",
                    size, os.path.basename(final_path), scope_hash, post_id)
        yield final_path
    finally:
        try:
            shutil.rmtree(tmp_dir, ignore_errors=True)
        except Exception:
            logger.warning("Failed to clean up %s", tmp_dir, exc_info=True)
