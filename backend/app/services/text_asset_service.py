"""Text description -> key assets worth turning into 3D props.

Complementary to VisionService: that service only ever sees what's in the
photo. This one reads the player's text description alone and pulls out
distinctive props the words call for that the photo may not show at all
(e.g. "with a giant floating whale statue"). Each extracted asset is later
submitted to Meshy Text-to-3D (see mesh_generation_service.submit_text).

Kept behind an interface for the same reason as VisionService:

- MockTextAssetService (default, no network, deterministic: extracts nothing).
- ClaudeTextAssetService (TEXT_ASSET_PROVIDER=claude): one Claude text-only
  call that returns TextAssetExtraction as structured output.
"""

from __future__ import annotations

import logging
from abc import ABC, abstractmethod

from pydantic import BaseModel, Field

logger = logging.getLogger(__name__)


class ExtractedAsset(BaseModel):
    """One prop named in the player's description, worth generating a mesh
    for even though it wasn't (necessarily) detected in the photo.

    `label` becomes WorldRecipe.objects[].type, `density` becomes
    objects[].density, and `prompt` is sent verbatim to Meshy Text-to-3D as
    the sole generation context -- it never mentions the racing game itself.
    """

    label: str = Field(min_length=1, max_length=40)
    prompt: str = Field(min_length=1, max_length=800)
    density: float = Field(ge=0.0, le=1.0)


class TextAssetExtraction(BaseModel):
    key_assets: list[ExtractedAsset] = Field(default_factory=list)


class TextAssetService(ABC):
    @abstractmethod
    def extract(self, description: str) -> TextAssetExtraction:
        raise NotImplementedError


class MockTextAssetService(TextAssetService):
    """Bootstrap/offline default: extracts nothing. No external call is
    made, so this requires no API key and no network."""

    def extract(self, description: str) -> TextAssetExtraction:
        return TextAssetExtraction(key_assets=[])


_CLAUDE_MODEL = "claude-opus-5"


class ClaudeTextAssetService(TextAssetService):
    """Real extractor: a single Claude call returning TextAssetExtraction as
    structured output. Reads ANTHROPIC_API_KEY (or an `ant auth login`
    profile) from the environment via the SDK's default client -- the same
    credential ClaudeVisionService uses."""

    def __init__(self, max_assets: int, client=None):
        import anthropic  # imported lazily so mock mode never needs the SDK

        self._anthropic = anthropic
        self._client = client or anthropic.Anthropic()
        self._max_assets = max_assets

    def extract(self, description: str) -> TextAssetExtraction:
        from app.prompts.text_asset_extraction import SYSTEM_PROMPT, build_user_prompt

        response = self._client.beta.messages.parse(
            model=_CLAUDE_MODEL,
            max_tokens=2048,
            system=SYSTEM_PROMPT.format(max_assets=self._max_assets),
            messages=[{"role": "user", "content": build_user_prompt(description)}],
            output_format=TextAssetExtraction,
            # Server-side refusal fallback: if the safety classifier declines
            # this request, it's re-routed instead of failing outright.
            betas=["server-side-fallback-2026-07-01"],
            fallbacks="default",
        )

        if response.stop_reason == "refusal" or response.parsed_output is None:
            raise RuntimeError(f"text asset extraction did not return a result (stop_reason={response.stop_reason})")

        extraction = response.parsed_output
        # The prompt asks for at most max_assets, but enforce it here too:
        # every extra asset is another (two-job) Meshy Text-to-3D generation.
        extraction.key_assets = extraction.key_assets[: self._max_assets]
        return extraction
