"""Motion-aware caption replacement via gpt-4o-mini multimodal on OpenRouter.

Replaces upstream's MiniCPM-V local model. Each segment:
  1. extract N frames from the video at the given frame_times (PIL images)
  2. upload them to S3 (so Vertex/OpenRouter can fetch them)
  3. ask gpt-4o-mini with motion-aware system prompt + image_url parts + transcript
  4. record the caption result

Signature preserved from upstream so videorag.py keeps working without changes.
"""
from __future__ import annotations

import hashlib
import logging
import os

import numpy as np
from moviepy.video.io.VideoFileClip import VideoFileClip
from PIL import Image
from tqdm import tqdm

from ..openrouter_helpers import chat_completions
from ..s3_frame_uploader import upload_pil_frames

logger = logging.getLogger("videorag.caption")


_MOTION_AWARE_SYSTEM = (
    "You are describing a 30-second video segment for retrieval search.\n"
    "You will see N still frames sampled in chronological order plus a transcript of the audio.\n"
    "Output ONE paragraph in English (regardless of transcript language).\n\n"
    "Be MOTION-aware: explicitly describe what changes between frames\n"
    "  - movement direction (left/right/up/down/in/out, slow/fast)\n"
    "  - camera moves (pan, zoom, cut, transition)\n"
    "  - scene/lighting/color shifts\n"
    "  - subject actions and gestures across frames\n\n"
    "Be AUDIO-aware: even though you can't hear the audio, infer from the transcript and visuals:\n"
    "  - speech tone if relevant\n"
    "  - likely music genre/mood (if no speech and transcript is empty/short)\n"
    "  - sound effects implied by visuals (laughter, applause, ambient noise)\n\n"
    "Be retrieval-rich: include concrete nouns, brand names visible in frames, visible text/OCR,\n"
    "color palette, mood descriptors. Avoid vague filler.\n"
    "Output the paragraph only — no headings, no bullet points."
)


def encode_video(video, frame_times):
    frames = []
    for t in frame_times:
        frames.append(video.get_frame(t))
    frames = np.stack(frames, axis=0)
    frames = [Image.fromarray(v.astype("uint8")).resize((1280, 720)) for v in frames]
    return frames


def _scope_hash_from_video_name(video_name: str) -> str:
    """We encode scope into the video filename (videorag_engine names it).
    Pattern: <scope_hash>__<post_id>. If we can't parse, fall back to a hash of the name.
    """
    if "__" in video_name:
        return video_name.split("__", 1)[0]
    return hashlib.sha256(video_name.encode("utf-8")).hexdigest()[:16]


def _post_id_from_video_name(video_name: str) -> str:
    if "__" in video_name:
        return video_name.split("__", 1)[1]
    return video_name


def _caption_with_openrouter(frame_urls: list[str], transcript: str,
                             refine_knowledge: str | None = None) -> str:
    if not frame_urls:
        return ""
    user_prefix = (
        f"Refine focus: {refine_knowledge}\n\n"
        if refine_knowledge else ""
    )
    user_text = (
        f"{user_prefix}"
        f"Transcript:\n\"\"\"{transcript or '(no speech detected)'}\"\"\"\n\n"
        f"Frames in chronological order:"
    )
    user_content: list[dict] = [{"type": "text", "text": user_text}]
    for u in frame_urls:
        user_content.append({"type": "image_url", "image_url": {"url": u}})

    messages = [
        {"role": "system", "content": _MOTION_AWARE_SYSTEM},
        {"role": "user", "content": user_content},
    ]
    return chat_completions(messages, temperature=0.4)


def segment_caption(video_name, video_path, segment_index2name, transcripts,
                    segment_times_info, caption_result, error_queue):
    """Populates caption_result[index] with motion-aware caption. Mirrors upstream signature.

    Runs in a multiprocessing.Process under spawn — env vars + module-level config in
    openrouter_helpers / s3_frame_uploader are picked up at import in the subprocess.
    """
    try:
        scope_hash = _scope_hash_from_video_name(video_name)
        post_id = _post_id_from_video_name(video_name)

        with VideoFileClip(video_path) as video:
            for index in tqdm(segment_index2name, desc=f"Captioning Video {video_name}"):
                frame_times = segment_times_info[index]["frame_times"]
                pil_frames = encode_video(video, frame_times)

                # Upload frames to S3 → URLs Gemini/gpt-4o-mini can fetch
                frame_urls = upload_pil_frames(
                    pil_frames,
                    scope_hash=scope_hash,
                    post_id=post_id,
                    segment_id=segment_index2name[index],
                )
                segment_transcript = transcripts.get(index, "") if isinstance(transcripts, dict) else transcripts[index]
                try:
                    caption = _caption_with_openrouter(frame_urls, segment_transcript)
                except Exception as ex:
                    logger.warning("caption failed for segment %s/%s: %s",
                                   video_name, index, ex)
                    caption = ""
                caption_result[index] = (caption or "").replace("\n", " ").replace("<|endoftext|>", "")
    except Exception as e:
        try:
            error_queue.put(f"Error in segment_caption:\n {str(e)}")
        except Exception:
            pass
        raise


def merge_segment_information(segment_index2name, segment_times_info, transcripts, captions):
    inserting_segments = {}
    for index in segment_index2name:
        inserting_segments[index] = {"content": None, "time": None}
        segment_name = segment_index2name[index]
        inserting_segments[index]["time"] = "-".join(segment_name.split("-")[-2:])
        inserting_segments[index]["content"] = (
            f"Caption:\n{captions[index]}\nTranscript:\n{transcripts[index]}\n\n"
        )
        inserting_segments[index]["transcript"] = transcripts[index]
        ftimes = segment_times_info[index]["frame_times"]
        inserting_segments[index]["frame_times"] = (
            ftimes.tolist() if hasattr(ftimes, "tolist") else list(ftimes)
        )
    return inserting_segments


def retrieved_segment_caption(caption_model, caption_tokenizer, refine_knowledge,
                              retrieved_segments, video_path_db, video_segments,
                              num_sampled_frames):
    """At query time, refine captions for retrieved segments with extra knowledge focus.

    `caption_model` and `caption_tokenizer` parameters are ignored (kept for upstream
    signature compatibility). They were used to feed MiniCPM-V; we ignore them and
    call gpt-4o-mini via OpenRouter instead.
    """
    caption_result = {}
    for this_segment in tqdm(retrieved_segments, desc="Captioning Retrieved Segments"):
        try:
            video_name = "_".join(this_segment.split("_")[:-1])
            index = this_segment.split("_")[-1]
            video_path = video_path_db._data[video_name]
            timestamp = video_segments._data[video_name][index]["time"].split("-")
            start, end = float(timestamp[0]), float(timestamp[1])
            video = VideoFileClip(video_path)
            try:
                frame_times = np.linspace(start, end, num_sampled_frames, endpoint=False)
                pil_frames = encode_video(video, frame_times)
            finally:
                video.close()

            scope_hash = _scope_hash_from_video_name(video_name)
            post_id = _post_id_from_video_name(video_name)
            frame_urls = upload_pil_frames(
                pil_frames,
                scope_hash=scope_hash,
                post_id=post_id,
                segment_id=f"{this_segment}_q",
            )
            transcript = video_segments._data[video_name][index]["transcript"]
            try:
                this_caption = _caption_with_openrouter(
                    frame_urls, transcript, refine_knowledge=refine_knowledge
                )
            except Exception as ex:
                logger.warning("retrieved caption failed for %s: %s", this_segment, ex)
                this_caption = ""
            this_caption = (this_caption or "").replace("\n", " ").replace("<|endoftext|>", "")
            caption_result[this_segment] = (
                f"Caption:\n{this_caption}\nTranscript:\n{transcript}\n\n"
            )
        except Exception as ex:
            logger.warning("retrieved_segment_caption skipped %s: %s", this_segment, ex)
            caption_result[this_segment] = ""
    return caption_result
