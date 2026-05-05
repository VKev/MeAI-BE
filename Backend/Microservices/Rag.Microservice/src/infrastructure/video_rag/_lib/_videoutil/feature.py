"""Segment embedding via Gemini Embedding 2 (multimodal) on OpenRouter.

Replaces upstream's ImageBind. Per segment we sample N frames, upload them to
S3, then call /embeddings with content=[image_url ...image_url, text=transcript]
to get one 3072-dim vector. Same vector space as text queries (text_embedding).

Signatures preserved so videorag.py keeps working: encode_video_segments(...)
and encode_string_query(...) both return objects exposing .cpu().numpy() so
upstream `torch.concat([x], dim=0).numpy()` calls continue to function.
"""
from __future__ import annotations

import hashlib
import logging
import os

import numpy as np
from moviepy.video.io.VideoFileClip import VideoFileClip
from PIL import Image
from tqdm import tqdm

from ..openrouter_helpers import multimodal_embedding, multimodal_embedding_batch, text_embedding
from ..s3_frame_uploader import upload_pil_frames

logger = logging.getLogger("videorag.feature")


def _frames_per_segment() -> int:
    return int(os.environ.get("VIDEORAG_FRAMES_PER_SEGMENT", "5"))


def _scope_hash_from_video_name(video_name: str) -> str:
    if "__" in video_name:
        return video_name.split("__", 1)[0]
    return hashlib.sha256(video_name.encode("utf-8")).hexdigest()[:16]


def _post_id_from_video_name(video_name: str) -> str:
    if "__" in video_name:
        return video_name.split("__", 1)[1]
    return video_name


def _sample_frames_from_segment(segment_path: str, n: int) -> list[Image.Image]:
    """Open a per-segment .mp4 and sample N evenly-spaced PIL frames."""
    pil_frames: list[Image.Image] = []
    with VideoFileClip(segment_path) as clip:
        duration = max(float(clip.duration or 0.0), 0.0)
        if duration <= 0.0 or n <= 0:
            return pil_frames
        # endpoint=False so we don't grab the last frame which is often black
        times = np.linspace(0.0, duration, n, endpoint=False)
        for t in times:
            arr = clip.get_frame(float(t))
            img = Image.fromarray(arr.astype("uint8")).resize((1280, 720))
            pil_frames.append(img)
    return pil_frames


class _NumpyAsTorchShim:
    """Tiny shim so upstream code calling `.cpu().numpy()` keeps working
    even though we no longer use torch on this path.
    """

    def __init__(self, arr: np.ndarray):
        self._arr = arr

    def cpu(self) -> "_NumpyAsTorchShim":
        return self

    def numpy(self) -> np.ndarray:
        return self._arr

    # Some callers may treat it as array-like
    def __array__(self) -> np.ndarray:
        return self._arr

    @property
    def shape(self):
        return self._arr.shape

    def __len__(self) -> int:
        return len(self._arr)


def _segment_meta_from_path(segment_path: str) -> tuple[str, str, str]:
    """video_name = parent dir name; segment_id = basename without ext."""
    seg_basename = os.path.splitext(os.path.basename(segment_path))[0]
    parent_dir = os.path.basename(os.path.dirname(segment_path)) or "unknown"
    scope_hash = _scope_hash_from_video_name(parent_dir)
    post_id = _post_id_from_video_name(parent_dir)
    return scope_hash, post_id, seg_basename


def encode_video_segments(video_paths, embedder=None):
    """Returns a shim wrapping a (len(video_paths), 3072) float32 ndarray.

    `embedder` is ignored (kept for upstream signature). Each path is one
    segment .mp4. For empty/failed segments we emit a zero vector so indices
    line up with the input list.
    """
    n_frames = _frames_per_segment()
    vectors: list[np.ndarray] = []
    for seg_path in tqdm(video_paths, desc="Embedding segments"):
        try:
            pil_frames = _sample_frames_from_segment(seg_path, n_frames)
            if not pil_frames:
                logger.warning("no frames sampled from %s — emitting zero vec", seg_path)
                vectors.append(np.zeros(3072, dtype=np.float32))
                continue
            scope_hash, post_id, seg_id = _segment_meta_from_path(seg_path)
            frame_urls = upload_pil_frames(
                pil_frames,
                scope_hash=scope_hash,
                post_id=post_id,
                segment_id=seg_id,
            )
            if not frame_urls:
                logger.warning("no frame URLs for %s — emitting zero vec", seg_path)
                vectors.append(np.zeros(3072, dtype=np.float32))
                continue
            vec = multimodal_embedding(image_urls=frame_urls, text=None)
            vectors.append(np.asarray(vec, dtype=np.float32))
        except Exception as ex:
            logger.warning("encode_video_segments failed for %s: %s — zero vec", seg_path, ex)
            vectors.append(np.zeros(3072, dtype=np.float32))
    arr = np.stack(vectors, axis=0) if vectors else np.zeros((0, 3072), dtype=np.float32)
    return _NumpyAsTorchShim(arr)


def encode_video_frames(video_paths) -> list[dict]:
    """Frame-level encoder. For each segment, sample N frames, upload them to S3,
    and embed EACH frame individually via Gemini multimodal (one row per frame).

    Returns a list of dicts in flat order (segment 0 frames 0..N, segment 1 frames 0..N, …):

        {
            "vector":          [3072 floats],
            "frame_url":       "https://s3.../scope/postId/segId/frame_0.jpg",
            "segment_path":    "/cache/.../seg_0.mp4",
            "frame_index":     0,
            "segment_id":      "seg_0",
            "scope_hash":      "facebook019dedac…",
            "post_id":         "1122…000000000",
        }

    Used by `vdb_qdrant.upsert` to create one Qdrant point per frame (not per segment),
    enabling true frame-level visual retrieval downstream — image-gen can later use the
    actual visually-matching frame as a reference, not just the video's static thumbnail.
    """
    n_frames = _frames_per_segment()
    out: list[dict] = []
    for seg_path in tqdm(video_paths, desc="Embedding frames"):
        try:
            pil_frames = _sample_frames_from_segment(seg_path, n_frames)
            if not pil_frames:
                logger.warning("no frames sampled from %s — skipping", seg_path)
                continue
            scope_hash, post_id, seg_id = _segment_meta_from_path(seg_path)
            frame_urls = upload_pil_frames(
                pil_frames,
                scope_hash=scope_hash,
                post_id=post_id,
                segment_id=seg_id,
            )
            if not frame_urls:
                logger.warning("no frame URLs for %s — skipping", seg_path)
                continue

            # Embed all N frames in ONE batched API call to Gemini — much cheaper
            # than N sequential calls when N=5.
            inputs = [
                [{"type": "image_url", "image_url": {"url": u}}]
                for u in frame_urls if u
            ]
            try:
                vectors = multimodal_embedding_batch(inputs)
            except Exception as ex:
                logger.warning(
                    "frame-batch embed failed for %s — falling back to per-frame: %s",
                    seg_path, ex,
                )
                vectors = []
                for u in frame_urls:
                    if not u:
                        vectors.append(np.zeros(3072, dtype=np.float32).tolist())
                        continue
                    try:
                        vectors.append(multimodal_embedding(image_urls=[u], text=None))
                    except Exception as inner:
                        logger.warning(
                            "single-frame embed failed for %s: %s — zero vec",
                            u, inner,
                        )
                        vectors.append(np.zeros(3072, dtype=np.float32).tolist())

            for f_idx, (url, vec) in enumerate(zip(frame_urls, vectors)):
                if not url:
                    continue
                out.append({
                    "vector": np.asarray(vec, dtype=np.float32),
                    "frame_url": url,
                    "segment_path": seg_path,
                    "frame_index": f_idx,
                    "segment_id": seg_id,
                    "scope_hash": scope_hash,
                    "post_id": post_id,
                })
        except Exception as ex:
            logger.warning("encode_video_frames failed for %s: %s — segment skipped", seg_path, ex)
    return out


def encode_string_query(query: str, embedder=None):
    """Text-only multimodal embed for queries. Returns a 1xD shim."""
    try:
        vec = text_embedding(query or "")
        arr = np.asarray([vec], dtype=np.float32)
    except Exception as ex:
        logger.warning("encode_string_query failed: %s — zero vec", ex)
        arr = np.zeros((1, 3072), dtype=np.float32)
    return _NumpyAsTorchShim(arr)
