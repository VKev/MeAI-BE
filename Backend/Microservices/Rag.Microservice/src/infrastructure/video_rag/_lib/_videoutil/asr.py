"""ASR replacement: OpenRouter Whisper-1 via /audio/transcriptions multipart.

Original used faster-whisper locally; we route through OpenRouter so no local
model weights are needed. Same function signature so videorag.py is unchanged.
"""
from __future__ import annotations

import logging
import os

from tqdm import tqdm

from ..openrouter_helpers import transcribe_audio

logger = logging.getLogger("videorag.asr")


def speech_to_text(video_name, working_dir, segment_index2name, audio_output_format):
    """Run STT on each segment audio file. Returns dict[index, transcript].

    Signature preserved from upstream so videorag.py.insert_video keeps working.
    Sequential (one OpenRouter call per segment) — OpenRouter is network-bound,
    not GPU-bound, so a small bound-concurrency loop would help but blocking
    sequential is fine for v1 and avoids subprocess/asyncio interactions.
    """
    cache_path = os.path.join(working_dir, "_cache", video_name)
    transcripts = {}
    for index in tqdm(segment_index2name, desc=f"Speech Recognition {video_name}"):
        segment_name = segment_index2name[index]
        audio_file = os.path.join(cache_path, f"{segment_name}.{audio_output_format}")
        if not os.path.exists(audio_file):
            transcripts[index] = ""
            continue
        try:
            with open(audio_file, "rb") as f:
                audio_bytes = f.read()
            text = transcribe_audio(
                audio_bytes,
                filename=os.path.basename(audio_file),
                content_type=f"audio/{audio_output_format}",
            )
            transcripts[index] = text
        except Exception as ex:
            logger.warning("STT failed on %s: %s", audio_file, ex)
            transcripts[index] = ""
    return transcripts
