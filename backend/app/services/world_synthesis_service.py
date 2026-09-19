"""Image understanding + user description -> WorldRecipe.

Kept behind an interface so the rest of the app never depends on a specific
LLM vendor. Only a mock implementation ships in this bootstrap; a real one
can replace it later without changing the /generate-world contract.
"""

from __future__ import annotations

import logging
from abc import ABC, abstractmethod

from app.models.world_recipe import (
    DEFAULT_PLACEMENT,
    ObjectAsset,
    TrackInfo,
    WorldInfo,
    WorldObjectEntry,
    WorldRecipe,
    derive_seed,
)
from app.services.mesh_generation_service import MeshTask
from app.services.vision_service import SceneUnderstanding

logger = logging.getLogger(__name__)


class WorldSynthesisService(ABC):
    @abstractmethod
    def synthesize(
        self,
        scene: SceneUnderstanding,
        description: str,
        mesh_tasks: list[MeshTask] | None = None,
    ) -> WorldRecipe:
        """`mesh_tasks` are the in-flight mesh generations for this world, one
        per detected object type that was successfully submitted; each becomes
        that object's `asset` handle in the recipe."""
        raise NotImplementedError


# Keyword -> theme profile. The mock picks the first profile whose keyword
# appears in the user's description (case-insensitive); "tropical" is the
# fallback/default so the exact example from docs/world-recipe.md is always
# a reachable, tested output. The profile's `objects` are only used when the
# vision service detected nothing in the image.
_THEME_PROFILES: dict[str, dict] = {
    "snow": dict(
        name="Frostbite Summit",
        theme="snowy mountain",
        terrain="snow",
        weather="snowy",
        time_of_day="day",
        objects=[
            WorldObjectEntry(type="pine_tree", density=0.6, placement="scattered"),
            WorldObjectEntry(type="rock", density=0.3, placement="background"),
        ],
        palette=["#A9C7DE", "#FFFFFF", "#6E7C8C"],
    ),
    "desert": dict(
        name="Dune Circuit",
        theme="desert canyon",
        terrain="sand",
        weather="sunny",
        time_of_day="day",
        objects=[
            WorldObjectEntry(type="cactus", density=0.4, placement="scattered"),
            WorldObjectEntry(type="rock", density=0.4, placement="landmark"),
        ],
        palette=["#E3B778", "#C2B280", "#8A6D3B"],
    ),
    "forest": dict(
        name="Emerald Trail",
        theme="dense forest",
        terrain="grass",
        weather="cloudy",
        time_of_day="day",
        objects=[
            WorldObjectEntry(type="tree", density=0.7, placement="scattered"),
            WorldObjectEntry(type="rock", density=0.1, placement="scattered"),
        ],
        palette=["#2D5A27", "#4C6B3A", "#6E8B3D"],
    ),
    "tropical": dict(
        name="Tropical Paradise",
        theme="tropical beach",
        terrain="sand",
        weather="sunny",
        time_of_day="day",
        objects=[
            WorldObjectEntry(type="palm_tree", density=0.5, placement="roadside"),
            WorldObjectEntry(type="rock", density=0.2, placement="scattered"),
        ],
        palette=["#2E8B57", "#F4D35E", "#2D9CDB"],
    ),
}

_DEFAULT_PROFILE_KEY = "tropical"


def _pick_profile_key(scene: SceneUnderstanding, description: str) -> str:
    lowered = description.lower()
    for keyword in _THEME_PROFILES:
        if keyword in lowered:
            return keyword
    for tag in scene.tags:
        if tag in _THEME_PROFILES:
            return tag
    return _DEFAULT_PROFILE_KEY


def _objects_from_scene(scene: SceneUnderstanding, mesh_tasks: list[MeshTask]) -> list[WorldObjectEntry]:
    """One recipe object per detected object kind, with its mesh task (if any)
    attached and the VLM's placement hint passed through.

    Ordered most-prominent first (stable, so ties keep VLM order) -- purely
    for readability; Unity keys its behaviour off ``placement``, not order.
    Duplicate labels collapse into their most prominent occurrence.

    The prompt asks for at most one "landmark"; enforce it here so Unity
    never has to arbitrate. The most prominent landmark wins, the rest
    become "scattered"."""
    task_by_type = {task.object_type: task for task in mesh_tasks}
    entries: list[WorldObjectEntry] = []
    seen: set[str] = set()
    landmark_taken = False
    for detected in sorted(scene.detected_objects, key=lambda d: d.prominence, reverse=True):
        if detected.label in seen:
            continue
        seen.add(detected.label)

        placement = detected.placement
        if placement == "landmark":
            if landmark_taken:
                logger.debug("demoting extra landmark %r to %s", detected.label, DEFAULT_PLACEMENT)
                placement = DEFAULT_PLACEMENT
            landmark_taken = True

        task = task_by_type.get(detected.label)
        entries.append(
            WorldObjectEntry(
                type=detected.label,
                density=detected.prominence,
                placement=placement,
                asset=ObjectAsset(task_id=task.task_id, provider=task.provider) if task else None,
            )
        )
    return entries


class MockWorldSynthesisService(WorldSynthesisService):
    """Deterministic mock: same (scene, description) always yields the same
    WorldRecipe (mesh task IDs aside). No external AI call is made."""

    def synthesize(
        self,
        scene: SceneUnderstanding,
        description: str,
        mesh_tasks: list[MeshTask] | None = None,
    ) -> WorldRecipe:
        profile_key = _pick_profile_key(scene, description)
        profile = _THEME_PROFILES[profile_key]
        seed = derive_seed(description, profile_key, "".join(scene.dominant_colors))

        objects = _objects_from_scene(scene, mesh_tasks or [])
        if not objects:
            objects = list(profile["objects"])

        return WorldRecipe(
            version=1,
            seed=seed,
            world=WorldInfo(
                name=profile["name"],
                theme=profile["theme"],
                terrain=profile["terrain"],
                weather=profile["weather"],
                time_of_day=profile["time_of_day"],
            ),
            track=TrackInfo(width=16.0, length=800.0, difficulty=0.5),
            objects=objects,
            palette=list(profile["palette"]),
        )
