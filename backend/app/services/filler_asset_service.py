"""Photo + text description -> generic filler keywords to search a public 3D
asset library for (Poly Pizza).

Distinct from both VisionService and TextAssetService: those two extract
*specific* objects (either actually in the photo, or vividly described by
the player) and turn each into its own generated mesh. This one instead
looks at the whole scene (image + text, like vision) and suggests *generic*,
common props that would plausibly exist in a public library and would help
fill out and diversify the world -- rocks, benches, traffic cones, crates --
never anything unique enough to need real generation. `keyword` is a search
term, not a generation prompt: it's sent to Poly Pizza's keyword search, not
to an image/text-to-3D model, so it should be simple and generic rather than
vivid and specific.

Kept behind an interface for the same reason as the other extraction
services:

- MockFillerAssetService (default, no network, deterministic: suggests
  nothing).
- ClaudeFillerAssetService (FILLER_ASSET_PROVIDER=claude): one Claude vision
  call that returns FillerAssetExtraction as structured output.
"""

from __future__ import annotations

import base64
import logging
from abc import ABC, abstractmethod

from pydantic import BaseModel, Field

from app.services.vision_service import Placement

logger = logging.getLogger(__name__)


class FillerAsset(BaseModel):
    """One generic prop suggested to help fill out the world, worth searching
    a public asset library for.

    `keyword` becomes the library search query (and, if a match is found,
    WorldRecipe.objects[].type); `density` becomes objects[].density;
    `placement` becomes objects[].placement. `placement` normally excludes
    "landmark" -- filler is generic, interchangeable clutter by definition,
    never the one centrepiece a landmark is meant to be -- but `suggest()`'s
    `max_landmarks` can lift that restriction (see docs/decisions/0010-personalize-toggle.md):
    when personalized (Meshy) generation is turned off, nothing else in the
    world can supply a landmark, so filler is allowed to nominate 1-2.
    """

    keyword: str = Field(min_length=1, max_length=40)
    density: float = Field(ge=0.0, le=1.0)
    placement: Placement = "scattered"


class FillerAssetExtraction(BaseModel):
    filler_assets: list[FillerAsset] = Field(default_factory=list)


class FillerAssetService(ABC):
    @abstractmethod
    def suggest(self, image_bytes: bytes, description: str, max_landmarks: int = 0) -> FillerAssetExtraction:
        """`max_landmarks` is normally 0 (filler is never a landmark); pass a
        positive value only when no other source in this world can supply
        one, to let filler nominate that many library-searchable centrepieces
        instead of leaving the world without one at all."""
        raise NotImplementedError


class MockFillerAssetService(FillerAssetService):
    """Bootstrap/offline default: suggests nothing. No external call is
    made, so this requires no API key and no network."""

    def suggest(self, image_bytes: bytes, description: str, max_landmarks: int = 0) -> FillerAssetExtraction:
        return FillerAssetExtraction(filler_assets=[])


_CLAUDE_MODEL = "claude-opus-5"


def _media_type_for(image_bytes: bytes) -> str:
    if image_bytes.startswith(b"\x89PNG"):
        return "image/png"
    return "image/jpeg"


class ClaudeFillerAssetService(FillerAssetService):
    """Real suggester: a single Claude vision call returning
    FillerAssetExtraction as structured output. Reads ANTHROPIC_API_KEY (or
    an `ant auth login` profile) from the environment via the SDK's default
    client -- the same credential ClaudeVisionService uses."""

    def __init__(self, max_assets: int, client=None):
        import anthropic  # imported lazily so mock mode never needs the SDK

        self._anthropic = anthropic
        self._client = client or anthropic.Anthropic()
        self._max_assets = max_assets

    def suggest(self, image_bytes: bytes, description: str, max_landmarks: int = 0) -> FillerAssetExtraction:
        from app.prompts.filler_asset_extraction import build_system_prompt, build_user_prompt

        image_b64 = base64.standard_b64encode(image_bytes).decode("utf-8")
        response = self._client.beta.messages.parse(
            model=_CLAUDE_MODEL,
            max_tokens=2048,
            system=build_system_prompt(self._max_assets, max_landmarks),
            messages=[
                {
                    "role": "user",
                    "content": [
                        {
                            "type": "image",
                            "source": {
                                "type": "base64",
                                "media_type": _media_type_for(image_bytes),
                                "data": image_b64,
                            },
                        },
                        {"type": "text", "text": build_user_prompt(description)},
                    ],
                }
            ],
            output_format=FillerAssetExtraction,
            # Server-side refusal fallback: if the safety classifier declines
            # this image, the request is re-routed instead of failing outright.
            betas=["server-side-fallback-2026-07-01"],
            fallbacks="default",
        )

        if response.stop_reason == "refusal" or response.parsed_output is None:
            raise RuntimeError(f"filler asset suggestion did not return a result (stop_reason={response.stop_reason})")

        extraction = response.parsed_output
        # The prompt asks for at most max_assets, but enforce it here too:
        # every extra asset is another Poly Pizza search call.
        extraction.filler_assets = extraction.filler_assets[: self._max_assets]

        # Same defense-in-depth for the landmark cap: demote any extra
        # "landmark" beyond max_landmarks rather than trusting the prompt
        # alone (the model_synthesis-side MAX_LANDMARKS demotion only
        # accounts for photo/text sources, not a misbehaving filler call).
        landmark_count = 0
        for asset in extraction.filler_assets:
            if asset.placement != "landmark":
                continue
            if landmark_count >= max_landmarks:
                asset.placement = "scattered"
            else:
                landmark_count += 1

        return extraction
