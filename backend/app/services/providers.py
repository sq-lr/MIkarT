"""Env-driven wiring of the AI/mesh providers.

    AI_PROVIDER    mock (default) | claude   -> VisionService
    MESH_PROVIDER  mock (default) | meshy    -> MeshGenerationService

Both default to mock so `pytest` and an offline demo need no keys. Selecting
a real provider without its key fails fast at startup with a clear message
rather than at the first player request. Tests swap implementations with
`override(...)`.
"""

from __future__ import annotations

import os
from dataclasses import dataclass

from app.services.mesh_generation_service import (
    MeshGenerationService,
    MeshTaskRegistry,
    MeshyMeshGenerationService,
    MockMeshGenerationService,
)
from app.services.text_asset_service import ClaudeTextAssetService, MockTextAssetService, TextAssetService
from app.services.vision_service import ClaudeVisionService, MockVisionService, VisionService
from app.services.world_synthesis_service import MockWorldSynthesisService, WorldSynthesisService

DEFAULT_MAX_OBJECTS_PER_WORLD = 4
# Each text asset costs TWO Meshy jobs (preview + refine), vs. one for an
# image crop, so this defaults lower than DEFAULT_MAX_OBJECTS_PER_WORLD.
DEFAULT_MAX_TEXT_ASSETS_PER_WORLD = 2


@dataclass
class Providers:
    vision: VisionService
    mesh: MeshGenerationService
    synthesis: WorldSynthesisService
    text_assets: TextAssetService
    registry: MeshTaskRegistry
    max_objects_per_world: int
    max_text_assets_per_world: int


def _env_bool(name: str, default: bool) -> bool:
    raw = os.environ.get(name)
    if raw is None or raw == "":
        return default
    return raw.strip().lower() in {"1", "true", "yes", "on"}


def build_from_env() -> Providers:
    max_objects = int(os.environ.get("MAX_OBJECTS_PER_WORLD", DEFAULT_MAX_OBJECTS_PER_WORLD))
    max_text_assets = int(os.environ.get("MAX_TEXT_ASSETS_PER_WORLD", DEFAULT_MAX_TEXT_ASSETS_PER_WORLD))

    ai_provider = os.environ.get("AI_PROVIDER", "mock").strip().lower()
    if ai_provider == "mock":
        vision: VisionService = MockVisionService()
    elif ai_provider == "claude":
        if not (os.environ.get("ANTHROPIC_API_KEY") or os.environ.get("ANTHROPIC_AUTH_TOKEN")):
            raise RuntimeError("AI_PROVIDER=claude requires ANTHROPIC_API_KEY (or ANTHROPIC_AUTH_TOKEN)")
        vision = ClaudeVisionService(max_objects=max_objects)
    else:
        raise RuntimeError(f"unknown AI_PROVIDER {ai_provider!r} (expected 'mock' or 'claude')")

    mesh_provider = os.environ.get("MESH_PROVIDER", "mock").strip().lower()
    if mesh_provider == "mock":
        mesh: MeshGenerationService = MockMeshGenerationService()
    elif mesh_provider == "meshy":
        api_key = os.environ.get("MESHY_API_KEY", "")
        if not api_key:
            raise RuntimeError("MESH_PROVIDER=meshy requires MESHY_API_KEY")
        mesh = MeshyMeshGenerationService(
            api_key=api_key,
            model_type=os.environ.get("MESHY_MODEL_TYPE", "standard"),
            should_texture=_env_bool("MESHY_SHOULD_TEXTURE", True),
            target_polycount=int(os.environ.get("MESHY_TARGET_POLYCOUNT", "8000")),
            text_geometry_resolution=os.environ.get("MESHY_TEXT_GEOMETRY_RESOLUTION", "standard"),
            text_texture_resolution=os.environ.get("MESHY_TEXT_TEXTURE_RESOLUTION", "2k"),
            text_enable_pbr=_env_bool("MESHY_TEXT_ENABLE_PBR", False),
        )
    else:
        raise RuntimeError(f"unknown MESH_PROVIDER {mesh_provider!r} (expected 'mock' or 'meshy')")

    text_asset_provider = os.environ.get("TEXT_ASSET_PROVIDER", "mock").strip().lower()
    if text_asset_provider == "mock":
        text_assets: TextAssetService = MockTextAssetService()
    elif text_asset_provider == "claude":
        if not (os.environ.get("ANTHROPIC_API_KEY") or os.environ.get("ANTHROPIC_AUTH_TOKEN")):
            raise RuntimeError("TEXT_ASSET_PROVIDER=claude requires ANTHROPIC_API_KEY (or ANTHROPIC_AUTH_TOKEN)")
        text_assets = ClaudeTextAssetService(max_assets=max_text_assets)
    else:
        raise RuntimeError(f"unknown TEXT_ASSET_PROVIDER {text_asset_provider!r} (expected 'mock' or 'claude')")

    return Providers(
        vision=vision,
        mesh=mesh,
        synthesis=MockWorldSynthesisService(),
        text_assets=text_assets,
        registry=MeshTaskRegistry(),
        max_objects_per_world=max_objects,
        max_text_assets_per_world=max_text_assets,
    )


_providers: Providers | None = None


def get_providers() -> Providers:
    global _providers
    if _providers is None:
        _providers = build_from_env()
    return _providers


def override(providers: Providers | None) -> None:
    """Test hook: replace (or with None, reset) the process-wide providers."""
    global _providers
    _providers = providers
