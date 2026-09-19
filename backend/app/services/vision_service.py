"""Image -> scene understanding.

Kept behind an interface so the rest of the app never depends on a specific
vision model vendor. Only a mock implementation ships in this bootstrap.
"""

from __future__ import annotations

import hashlib
from abc import ABC, abstractmethod

from pydantic import BaseModel


class SceneUnderstanding(BaseModel):
    dominant_colors: list[str]
    brightness: float
    tags: list[str]


# Deterministic canned profiles the mock picks between, keyed by a hash of the
# image bytes. A real implementation would replace this whole class.
_MOCK_PROFILES: list[SceneUnderstanding] = [
    SceneUnderstanding(dominant_colors=["#2E8B57", "#F4D35E", "#2D9CDB"], brightness=0.8, tags=["beach", "tropical", "water"]),
    SceneUnderstanding(dominant_colors=["#FFFFFF", "#A9C7DE", "#6E7C8C"], brightness=0.9, tags=["snow", "mountain", "cold"]),
    SceneUnderstanding(dominant_colors=["#C2B280", "#E3B778", "#8A6D3B"], brightness=0.7, tags=["desert", "sand", "dry"]),
    SceneUnderstanding(dominant_colors=["#2D5A27", "#4C6B3A", "#6E8B3D"], brightness=0.5, tags=["forest", "grass", "green"]),
]


class VisionService(ABC):
    @abstractmethod
    def analyze_image(self, image_bytes: bytes) -> SceneUnderstanding:
        raise NotImplementedError


class MockVisionService(VisionService):
    """Deterministically picks a canned scene profile from the image bytes.

    No external call is made, so this requires no API key and no network.
    """

    def analyze_image(self, image_bytes: bytes) -> SceneUnderstanding:
        digest = hashlib.sha256(image_bytes).digest()
        index = digest[0] % len(_MOCK_PROFILES)
        return _MOCK_PROFILES[index]
