"""Image -> scene understanding, including the objects to turn into 3D props.

Kept behind an interface so the rest of the app never depends on a specific
vision model vendor. Two implementations ship:

- MockVisionService (default, no network, deterministic).
- ClaudeVisionService (AI_PROVIDER=claude): one Claude vision call that returns
  the whole SceneUnderstanding as structured output.
"""

from __future__ import annotations

import base64
import hashlib
import logging
from abc import ABC, abstractmethod
from typing import Literal

from pydantic import BaseModel, Field, field_validator

logger = logging.getLogger(__name__)

# A Literal (not a str + validator) so that when SceneUnderstanding is used as
# Claude's structured-output schema the model is constrained to these values.
# Keep in sync with PLACEMENT_VALUES in app.models.world_recipe.
Placement = Literal["landmark", "roadside", "background", "scattered"]


class DetectedObject(BaseModel):
    """One kind of object the VLM found in the source image.

    `bbox` is normalized [x, y, width, height] in 0..1, origin top-left. It is
    cropped out of the source image (see object_cropper) and sent to the mesh
    provider; `label` becomes WorldRecipe.objects[].type, `prominence`
    becomes objects[].density and `placement` becomes objects[].placement --
    the VLM's judgement of how the object should be used in the world
    (see the prompt in app.prompts.object_extraction).
    """

    label: str = Field(min_length=1, max_length=40)
    bbox: list[float] = Field(min_length=4, max_length=4)
    prominence: float = Field(ge=0.0, le=1.0)
    placement: Placement = "scattered"

    @field_validator("bbox")
    @classmethod
    def _bbox_in_unit_square(cls, v: list[float]) -> list[float]:
        x, y, w, h = v
        if not (0.0 <= x <= 1.0 and 0.0 <= y <= 1.0):
            raise ValueError("bbox origin must be within 0..1")
        if w <= 0.0 or h <= 0.0:
            raise ValueError("bbox width/height must be positive")
        return [x, y, min(w, 1.0 - x), min(h, 1.0 - y)]


class SceneUnderstanding(BaseModel):
    dominant_colors: list[str]
    brightness: float
    tags: list[str]
    detected_objects: list[DetectedObject] = Field(default_factory=list)


# Deterministic canned profiles the mock picks between, keyed by a hash of the
# image bytes. The detected_objects are made up (they don't correspond to
# anything in the real image) but exercise the crop -> mesh pipeline offline,
# and between them cover every placement value so Unity's placement paths
# can be tested without a real VLM.
_MOCK_PROFILES: list[SceneUnderstanding] = [
    SceneUnderstanding(
        dominant_colors=["#2E8B57", "#F4D35E", "#2D9CDB"],
        brightness=0.8,
        tags=["beach", "tropical", "water"],
        detected_objects=[
            DetectedObject(label="palm_tree", bbox=[0.10, 0.10, 0.30, 0.70], prominence=0.5, placement="roadside"),
            DetectedObject(label="rock", bbox=[0.60, 0.60, 0.25, 0.25], prominence=0.2, placement="scattered"),
        ],
    ),
    SceneUnderstanding(
        dominant_colors=["#FFFFFF", "#A9C7DE", "#6E7C8C"],
        brightness=0.9,
        tags=["snow", "mountain", "cold"],
        detected_objects=[
            DetectedObject(label="pine_tree", bbox=[0.15, 0.05, 0.25, 0.80], prominence=0.6, placement="scattered"),
            DetectedObject(label="rock", bbox=[0.55, 0.65, 0.30, 0.25], prominence=0.3, placement="background"),
        ],
    ),
    SceneUnderstanding(
        dominant_colors=["#C2B280", "#E3B778", "#8A6D3B"],
        brightness=0.7,
        tags=["desert", "sand", "dry"],
        detected_objects=[
            DetectedObject(label="cactus", bbox=[0.40, 0.20, 0.20, 0.60], prominence=0.4, placement="scattered"),
            DetectedObject(label="rock", bbox=[0.05, 0.70, 0.25, 0.20], prominence=0.4, placement="landmark"),
        ],
    ),
    SceneUnderstanding(
        dominant_colors=["#2D5A27", "#4C6B3A", "#6E8B3D"],
        brightness=0.5,
        tags=["forest", "grass", "green"],
        detected_objects=[
            DetectedObject(label="tree", bbox=[0.20, 0.00, 0.35, 0.90], prominence=0.7, placement="scattered"),
            DetectedObject(label="bush", bbox=[0.65, 0.60, 0.30, 0.30], prominence=0.3, placement="scattered"),
        ],
    ),
]


class VisionService(ABC):
    @abstractmethod
    def analyze_image(self, image_bytes: bytes, description: str) -> SceneUnderstanding:
        raise NotImplementedError


class MockVisionService(VisionService):
    """Deterministically picks a canned scene profile from the image bytes.

    No external call is made, so this requires no API key and no network.
    """

    def analyze_image(self, image_bytes: bytes, description: str) -> SceneUnderstanding:
        digest = hashlib.sha256(image_bytes).digest()
        index = digest[0] % len(_MOCK_PROFILES)
        return _MOCK_PROFILES[index]


_CLAUDE_MODEL = "claude-opus-5"


def _media_type_for(image_bytes: bytes) -> str:
    if image_bytes.startswith(b"\x89PNG"):
        return "image/png"
    return "image/jpeg"


class ClaudeVisionService(VisionService):
    """Real VLM: a single Claude vision call returning SceneUnderstanding as
    structured output. Reads ANTHROPIC_API_KEY (or an `ant auth login`
    profile) from the environment via the SDK's default client."""

    def __init__(self, max_objects: int, client=None):
        import anthropic  # imported lazily so mock mode never needs the SDK

        self._anthropic = anthropic
        self._client = client or anthropic.Anthropic()
        self._max_objects = max_objects

    def analyze_image(self, image_bytes: bytes, description: str) -> SceneUnderstanding:
        from app.prompts.object_extraction import SYSTEM_PROMPT, build_user_prompt

        image_b64 = base64.standard_b64encode(image_bytes).decode("utf-8")
        response = self._client.beta.messages.parse(
            model=_CLAUDE_MODEL,
            max_tokens=4096,
            system=SYSTEM_PROMPT.format(max_objects=self._max_objects),
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
            output_format=SceneUnderstanding,
            # Server-side refusal fallback: if the safety classifier declines
            # this image, the request is re-routed instead of failing outright.
            betas=["server-side-fallback-2026-07-01"],
            fallbacks="default",
        )

        if response.stop_reason == "refusal" or response.parsed_output is None:
            raise RuntimeError(f"vision model did not return a scene (stop_reason={response.stop_reason})")

        scene = response.parsed_output
        # The prompt asks for at most max_objects, but enforce it here too:
        # every extra object is another mesh-generation task (credits + time).
        scene.detected_objects = scene.detected_objects[: self._max_objects]
        return scene
