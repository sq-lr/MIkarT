"""Backend-side mirror of Unity's AssetResolver boundary.

Not wired into /generate-world yet. This exists so that, later, the backend
can suggest external asset-pack IDs per object type/theme without changing
the WorldRecipe contract — Unity's AssetResolver would consume these
suggestions the same way it resolves local placeholders today.
"""

from __future__ import annotations

from abc import ABC, abstractmethod

from pydantic import BaseModel

from app.models.world_recipe import WorldRecipe


class AssetSuggestion(BaseModel):
    object_type: str
    suggested_asset_id: str | None = None


class AssetService(ABC):
    @abstractmethod
    def suggest_assets(self, recipe: WorldRecipe) -> list[AssetSuggestion]:
        raise NotImplementedError


class NoopAssetService(AssetService):
    """Bootstrap placeholder: no external asset lookup, Unity uses its own
    local AssetResolver fallbacks for every object type."""

    def suggest_assets(self, recipe: WorldRecipe) -> list[AssetSuggestion]:
        return []
