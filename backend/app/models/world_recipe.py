"""Pydantic models mirroring schemas/world_recipe.schema.json.

This module is the Python half of the AI <-> Unity contract. Keep it in sync
with the JSON Schema by hand; `tests/test_world_recipe.py` checks that the
mock output validates against both.
"""

from __future__ import annotations

import hashlib
import re

from pydantic import BaseModel, Field, field_validator, model_validator

_HEX_COLOR_RE = re.compile(r"^#[0-9A-Fa-f]{6}$")

_TERRAIN_VALUES = {"sand", "grass", "snow", "dirt", "rock", "mud"}
_WEATHER_VALUES = {"sunny", "rainy", "cloudy", "snowy", "clear"}
_TIME_OF_DAY_VALUES = {"day", "night", "dusk", "dawn"}


def _clamp(value: float, lo: float, hi: float) -> float:
    return max(lo, min(hi, value))


def derive_seed(*parts: str) -> int:
    """Deterministically derive a seed from arbitrary string parts.

    Used whenever an upstream seed is missing, so "same input -> same world"
    still holds instead of falling back to a random or fixed value.
    """
    digest = hashlib.sha256("|".join(parts).encode("utf-8")).hexdigest()
    return int(digest[:8], 16)


class WorldInfo(BaseModel):
    name: str = Field(min_length=1, max_length=80)
    theme: str = Field(min_length=1, max_length=80)
    terrain: str
    weather: str
    time_of_day: str

    @field_validator("terrain")
    @classmethod
    def _valid_terrain(cls, v: str) -> str:
        if v not in _TERRAIN_VALUES:
            raise ValueError(f"terrain must be one of {sorted(_TERRAIN_VALUES)}")
        return v

    @field_validator("weather")
    @classmethod
    def _valid_weather(cls, v: str) -> str:
        if v not in _WEATHER_VALUES:
            raise ValueError(f"weather must be one of {sorted(_WEATHER_VALUES)}")
        return v

    @field_validator("time_of_day")
    @classmethod
    def _valid_time_of_day(cls, v: str) -> str:
        if v not in _TIME_OF_DAY_VALUES:
            raise ValueError(f"time_of_day must be one of {sorted(_TIME_OF_DAY_VALUES)}")
        return v


class TrackInfo(BaseModel):
    width: float = Field(gt=0)
    length: float = Field(gt=0)
    difficulty: float

    @field_validator("difficulty")
    @classmethod
    def _clamp_difficulty(cls, v: float) -> float:
        return _clamp(v, 0.0, 1.0)


class WorldObjectEntry(BaseModel):
    type: str = Field(min_length=1, max_length=40)
    density: float

    @field_validator("density")
    @classmethod
    def _clamp_density(cls, v: float) -> float:
        return _clamp(v, 0.0, 1.0)


class WorldRecipe(BaseModel):
    version: int = 1
    seed: int | None = None
    world: WorldInfo
    track: TrackInfo
    objects: list[WorldObjectEntry] = Field(default_factory=list, max_length=12)
    palette: list[str] = Field(min_length=1, max_length=8)

    @field_validator("palette")
    @classmethod
    def _valid_palette(cls, v: list[str]) -> list[str]:
        for color in v:
            if not _HEX_COLOR_RE.match(color):
                raise ValueError(f"invalid hex color: {color!r}")
        return v

    @model_validator(mode="after")
    def _default_seed(self) -> "WorldRecipe":
        if self.seed is None:
            # Missing seed: derive deterministically from world fields rather
            # than randomizing, so identical input always yields the same seed.
            object.__setattr__(
                self, "seed", derive_seed(self.world.name, self.world.theme)
            )
        return self


class WorldRecipeResponse(BaseModel):
    world_recipe: WorldRecipe
