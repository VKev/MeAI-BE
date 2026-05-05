"""Vision-LLM describe-an-image adapter — implements `VisionDescriberPort`.

Used by the `kind=image` ingest path: take an image URL or data URL, ask the
chat model to describe it for semantic-search retrievability, return the text.
"""
from __future__ import annotations

import logging

from openai import AsyncOpenAI

logger = logging.getLogger("rag-service.vision-describer")


_DEFAULT_PROMPT = (
    "Describe this image thoroughly so it can later be retrieved by semantic search. "
    "Cover: visible text/OCR, objects, scenes, people (no PII guesses), colors, mood, "
    "and any notable details. Be concise but complete."
)


class OpenAIVisionDescriber:
    """Implements `VisionDescriberPort`. Requires a vision-capable chat model
    (gpt-4o-mini works; pure text models will reject the image_url part)."""

    def __init__(
        self,
        *,
        base_url: str,
        api_key: str,
        model: str,
        max_tokens: int = 600,
    ) -> None:
        self._client = AsyncOpenAI(api_key=api_key, base_url=base_url)
        self._model = model
        self._max_tokens = max_tokens

    async def describe(
        self, image_ref: str, custom_prompt: str | None = None
    ) -> str:
        prompt = custom_prompt or _DEFAULT_PROMPT
        response = await self._client.chat.completions.create(
            model=self._model,
            messages=[
                {
                    "role": "user",
                    "content": [
                        {"type": "text", "text": prompt},
                        {"type": "image_url", "image_url": {"url": image_ref}},
                    ],
                }
            ],
            max_tokens=self._max_tokens,
        )
        return (response.choices[0].message.content or "").strip()
