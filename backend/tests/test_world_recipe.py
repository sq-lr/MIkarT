from __future__ import annotations

import json
from pathlib import Path

import jsonschema
import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError

from app.main import app
from app.models.world_recipe import WorldRecipe
from app.services.vision_service import SceneUnderstanding
from app.services.world_synthesis_service import MockWorldSynthesisService

SCHEMA_PATH = Path(__file__).resolve().parents[2] / "schemas" / "world_recipe.schema.json"

VALID_RECIPE = {
    "version": 1,
    "seed": 482913,
    "world": {
        "name": "Tropical Paradise",
        "theme": "tropical beach",
        "terrain": "sand",
        "weather": "sunny",
        "time_of_day": "day",
    },
    "track": {"width": 8.0, "length": 800.0, "difficulty": 0.5},
    "objects": [
        {"type": "palm_tree", "density": 0.5},
        {"type": "rock", "density": 0.2},
    ],
    "palette": ["#2E8B57", "#F4D35E", "#2D9CDB"],
}


def _load_schema() -> dict:
    return json.loads(SCHEMA_PATH.read_text())


def test_valid_recipe_accepted():
    recipe = WorldRecipe.model_validate(VALID_RECIPE)
    assert recipe.world.name == "Tropical Paradise"
    assert recipe.seed == 482913
    jsonschema.validate(json.loads(recipe.model_dump_json()), _load_schema())


def test_invalid_density_normalized():
    data = json.loads(json.dumps(VALID_RECIPE))
    data["objects"] = [{"type": "palm_tree", "density": 1.7}, {"type": "rock", "density": -0.3}]
    recipe = WorldRecipe.model_validate(data)
    assert recipe.objects[0].density == 1.0
    assert recipe.objects[1].density == 0.0


def test_missing_seed_handled_deterministically():
    data = json.loads(json.dumps(VALID_RECIPE))
    del data["seed"]

    recipe_a = WorldRecipe.model_validate(data)
    recipe_b = WorldRecipe.model_validate(data)

    assert recipe_a.seed is not None
    assert recipe_a.seed == recipe_b.seed


def test_missing_world_fields_rejected():
    data = json.loads(json.dumps(VALID_RECIPE))
    del data["world"]
    with pytest.raises(ValidationError):
        WorldRecipe.model_validate(data)


def test_generate_world_endpoint_returns_valid_recipe():
    client = TestClient(app)
    # Minimal valid 1x1 PNG.
    png_bytes = bytes.fromhex(
        "89504e470d0a1a0a0000000d4948445200000001000000010806000000"
        "1f15c4890000000a49444154789c6360000002000155f7f4b40000000049454e44ae426082"
    )

    response = client.post(
        "/generate-world",
        files={"image": ("test.png", png_bytes, "image/png")},
        data={"description": "a chaotic tropical paradise"},
    )

    assert response.status_code == 200
    body = response.json()
    assert "world_recipe" in body
    jsonschema.validate(body["world_recipe"], _load_schema())


def test_synthesis_is_deterministic():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(dominant_colors=["#2E8B57"], brightness=0.8, tags=["beach"])

    recipe_a = service.synthesize(scene, "make it a beach paradise")
    recipe_b = service.synthesize(scene, "make it a beach paradise")

    assert recipe_a.model_dump() == recipe_b.model_dump()
