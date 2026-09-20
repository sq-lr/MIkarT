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
from app.services.filler_asset_service import FillerAsset
from app.services.mesh_generation_service import MeshTask
from app.services.text_asset_service import ExtractedAsset
from app.services.vision_service import SceneUnderstanding

logger = logging.getLogger(__name__)

# Mirrors WorldRecipe.objects' max_length -- keep in sync by hand, same as
# every other schema constant duplicated between app.models.world_recipe and
# schemas/world_recipe.schema.json. This is a hard ceiling: MAX_ASSETS_PER_WORLD
# (see providers.py) is configurable, but synthesize() clamps it to this, since
# anything higher would make WorldRecipe fail its own schema validation.
_SCHEMA_MAX_RECIPE_OBJECTS = 12

# Keep in sync with the "at most MAX_LANDMARKS objects may be 'landmark'"
# rule in both app.prompts.object_extraction and
# app.prompts.text_asset_extraction. Enforced again here because the two
# extraction calls are independent and each caps itself only within its own
# output -- combined, they could otherwise produce twice this many.
MAX_LANDMARKS = 2


class WorldSynthesisService(ABC):
    @abstractmethod
    def synthesize(
        self,
        scene: SceneUnderstanding,
        description: str,
        mesh_tasks: list[MeshTask] | None = None,
        text_assets: list[ExtractedAsset] | None = None,
        text_mesh_tasks: list[MeshTask] | None = None,
        filler_assets: list[FillerAsset] | None = None,
        filler_mesh_tasks: list[MeshTask] | None = None,
        sky: str = "sunny",
        max_assets: int = _SCHEMA_MAX_RECIPE_OBJECTS,
    ) -> WorldRecipe:
        """`mesh_tasks` are the in-flight mesh generations for this world, one
        per detected object type that was successfully submitted; each becomes
        that object's `asset` handle in the recipe. `text_assets` are the
        props extracted from the player's description alone (not necessarily
        in the photo); `text_mesh_tasks` are the in-flight generations for
        those, matched to `text_assets` by label the same way `mesh_tasks` is
        matched to `scene.detected_objects`. `filler_assets` are generic
        props suggested to diversify the world; `filler_mesh_tasks` are the
        library lookups for those that were actually found -- a filler asset
        with no match is dropped entirely rather than kept without one.
        `max_assets` is the configured overall cap (providers.max_assets_per_world),
        clamped to the schema's hard ceiling."""
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
        palette=["#0B3B32", "#2E7D5B", "#91C83E"],
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

    The prompt asks for at most MAX_LANDMARKS "landmark"s; enforce it here so
    Unity never has to arbitrate. The most prominent landmarks win, the rest
    become "scattered"."""
    task_by_type = {task.object_type: task for task in mesh_tasks}
    entries: list[WorldObjectEntry] = []
    seen: set[str] = set()
    landmark_count = 0
    for detected in sorted(scene.detected_objects, key=lambda d: d.prominence, reverse=True):
        if detected.label in seen:
            continue
        seen.add(detected.label)

        placement = detected.placement
        if placement == "landmark":
            if landmark_count >= MAX_LANDMARKS:
                logger.debug("demoting extra landmark %r to %s", detected.label, DEFAULT_PLACEMENT)
                placement = DEFAULT_PLACEMENT
            else:
                landmark_count += 1

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


def _objects_from_text_assets(
    text_assets: list[ExtractedAsset],
    text_mesh_tasks: list[MeshTask],
    taken_labels: set[str],
    landmark_count: int,
) -> list[WorldObjectEntry]:
    """One entry per extracted asset, skipping any label already claimed by a
    photo-detected object -- photo-derived wins on collision, since the text
    extraction never sees the photo and can't know it's about to duplicate
    something actually in the player's picture.

    `landmark_count` is how many photo-detected objects already claimed
    "landmark" -- the text extractor's own prompt caps it at MAX_LANDMARKS
    per call, but that call has no idea what the (independent) vision call
    decided, so the two combined could exceed it. Demote here, same as
    `_objects_from_scene` does within its own call, so Unity never sees more
    than MAX_LANDMARKS "landmark"s total.
    """
    task_by_label = {task.object_type: task for task in text_mesh_tasks}
    entries: list[WorldObjectEntry] = []
    seen = set(taken_labels)
    for asset in text_assets:
        if asset.label in seen:
            logger.info("text asset %r collides with a photo-detected object; skipping", asset.label)
            continue
        seen.add(asset.label)

        placement = asset.placement
        if placement == "landmark":
            if landmark_count >= MAX_LANDMARKS:
                logger.debug("demoting text asset landmark %r to %s", asset.label, DEFAULT_PLACEMENT)
                placement = DEFAULT_PLACEMENT
            else:
                landmark_count += 1

        task = task_by_label.get(asset.label)
        entries.append(
            WorldObjectEntry(
                type=asset.label,
                density=asset.density,
                placement=placement,
                asset=ObjectAsset(task_id=task.task_id, provider=task.provider) if task else None,
            )
        )
    return entries


def _objects_from_filler_assets(
    filler_assets: list[FillerAsset],
    filler_mesh_tasks: list[MeshTask],
    taken_labels: set[str],
) -> list[WorldObjectEntry]:
    """One entry per filler keyword that was actually found in the library.

    Unlike photo/text objects, a filler asset that found no match is dropped
    entirely rather than kept with no asset: the whole point of filler is a
    real library mesh, so a bare placeholder with a random generic keyword
    adds nothing a photo/text object placeholder wouldn't already. Skips any
    label already claimed by a photo- or text-derived object, same collision
    rule as text vs. photo. Placement is never "landmark" here -- filler_assets
    is restricted to that vocabulary upstream (FillerPlacement), so nothing to
    demote/count against MAX_LANDMARKS.
    """
    task_by_label = {task.object_type: task for task in filler_mesh_tasks}
    entries: list[WorldObjectEntry] = []
    seen = set(taken_labels)
    for asset in filler_assets:
        if asset.keyword in seen:
            logger.info("filler asset %r collides with an existing object; skipping", asset.keyword)
            continue
        task = task_by_label.get(asset.keyword)
        if task is None:
            continue  # no library match for this keyword -- nothing to add
        seen.add(asset.keyword)
        entries.append(
            WorldObjectEntry(
                type=asset.keyword,
                density=asset.density,
                placement=asset.placement,
                asset=ObjectAsset(task_id=task.task_id, provider=task.provider),
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
        text_assets: list[ExtractedAsset] | None = None,
        text_mesh_tasks: list[MeshTask] | None = None,
        filler_assets: list[FillerAsset] | None = None,
        filler_mesh_tasks: list[MeshTask] | None = None,
        sky: str = "sunny",
        max_assets: int = _SCHEMA_MAX_RECIPE_OBJECTS,
    ) -> WorldRecipe:
        max_assets = min(max_assets, _SCHEMA_MAX_RECIPE_OBJECTS)
        profile_key = _pick_profile_key(scene, description)
        profile = _THEME_PROFILES[profile_key]
        seed = derive_seed(description, profile_key, "".join(scene.dominant_colors))

        photo_objects = _objects_from_scene(scene, mesh_tasks or [])
        text_objects = _objects_from_text_assets(
            text_assets or [],
            text_mesh_tasks or [],
            {o.type for o in photo_objects},
            landmark_count=sum(1 for o in photo_objects if o.placement == "landmark"),
        )
        filler_objects = _objects_from_filler_assets(
            filler_assets or [],
            filler_mesh_tasks or [],
            {o.type for o in photo_objects} | {o.type for o in text_objects},
        )
        objects = photo_objects + text_objects + filler_objects
        if not objects:
            objects = list(profile["objects"])
        if len(objects) > max_assets:
            logger.warning("world has %d objects, truncating to %d", len(objects), max_assets)
            objects = objects[:max_assets]

        return WorldRecipe(
            version=1,
            seed=seed,
            world=WorldInfo(
                name=profile["name"],
                theme=profile["theme"],
                terrain=profile["terrain"],
                weather=profile["weather"],
                time_of_day=profile["time_of_day"],
                sky=sky,
                mood=scene.mood,
            ),
            track=TrackInfo(width=16.0, length=800.0, difficulty=0.5, surface=scene.track_surface),
            objects=objects,
            palette=list(profile["palette"]),
        )
