from __future__ import annotations

import json
from pathlib import Path

import jsonschema
import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError

from app.main import app
from app.models.world_recipe import WorldRecipe
from app.services.mesh_generation_service import MeshTask
from app.services.text_asset_service import ExtractedAsset
from app.services.vision_service import DetectedObject, SceneUnderstanding
from app.services.world_synthesis_service import MockWorldSynthesisService
from tests.conftest import make_png

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
        {"type": "palm_tree", "density": 0.5, "asset": {"task_id": "0193a0c1-abcd", "provider": "meshy"}},
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


def test_asset_is_optional_and_validated():
    data = json.loads(json.dumps(VALID_RECIPE))
    del data["objects"][0]["asset"]
    recipe = WorldRecipe.model_validate(data)
    assert recipe.objects[0].asset is None
    jsonschema.validate(json.loads(recipe.model_dump_json(exclude_none=True)), _load_schema())

    data["objects"][0]["asset"] = {"task_id": "x", "provider": "unknown_vendor"}
    with pytest.raises(ValidationError):
        WorldRecipe.model_validate(data)


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

    response = client.post(
        "/generate-world",
        files={"image": ("test.png", make_png(), "image/png")},
        data={"description": "a chaotic tropical paradise"},
    )

    assert response.status_code == 200
    body = response.json()
    assert "world_recipe" in body
    jsonschema.validate(body["world_recipe"], _load_schema())

    # Mock vision always detects something, and the mock mesh provider never
    # issues tasks, so objects are present but carry no asset handles.
    objects = body["world_recipe"]["objects"]
    assert objects
    assert all("asset" not in obj for obj in objects)


def test_undecodable_image_still_generates_a_world():
    client = TestClient(app)
    response = client.post(
        "/generate-world",
        files={"image": ("junk.png", b"\x89PNG not really", "image/png")},
        data={"description": "snowy peaks"},
    )
    assert response.status_code == 200
    jsonschema.validate(response.json()["world_recipe"], _load_schema())


def test_synthesis_is_deterministic():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(dominant_colors=["#2E8B57"], brightness=0.8, tags=["beach"])

    recipe_a = service.synthesize(scene, "make it a beach paradise")
    recipe_b = service.synthesize(scene, "make it a beach paradise")

    assert recipe_a.model_dump() == recipe_b.model_dump()
    # No detections -> the theme profile's canned objects are used.
    assert [o.type for o in recipe_a.objects] == ["palm_tree", "rock"]


def test_synthesis_uses_detected_objects_and_attaches_mesh_tasks():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(
        dominant_colors=["#2E8B57"],
        brightness=0.8,
        tags=["beach"],
        detected_objects=[
            DetectedObject(label="lantern", bbox=[0.1, 0.1, 0.2, 0.4], prominence=0.35),
            DetectedObject(label="rock", bbox=[0.5, 0.5, 0.3, 0.3], prominence=0.2),
            DetectedObject(label="rock", bbox=[0.7, 0.7, 0.1, 0.1], prominence=0.9),  # duplicate kind
        ],
    )
    tasks = [MeshTask(task_id="task-lantern", object_type="lantern", provider="meshy")]

    recipe = service.synthesize(scene, "beach", tasks)

    assert [(o.type, o.density) for o in recipe.objects] == [("lantern", 0.35), ("rock", 0.2)]
    assert recipe.objects[0].asset is not None and recipe.objects[0].asset.task_id == "task-lantern"
    assert recipe.objects[1].asset is None
    jsonschema.validate(json.loads(recipe.model_dump_json(exclude_none=True)), _load_schema())


def test_synthesis_merges_text_assets_after_photo_objects():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(
        dominant_colors=["#2E8B57"],
        brightness=0.8,
        tags=["beach"],
        detected_objects=[DetectedObject(label="lantern", bbox=[0.1, 0.1, 0.2, 0.4], prominence=0.35)],
    )
    photo_tasks = [MeshTask(task_id="task-lantern", object_type="lantern", provider="meshy")]
    text_assets = [ExtractedAsset(label="whale_statue", prompt="a giant stone whale", density=0.15)]
    text_tasks = [MeshTask(task_id="text-whale_statue", object_type="whale_statue", provider="meshy")]

    recipe = service.synthesize(scene, "beach", photo_tasks, text_assets, text_tasks)

    assert [(o.type, o.density) for o in recipe.objects] == [("lantern", 0.35), ("whale_statue", 0.15)]
    assert recipe.objects[1].asset is not None and recipe.objects[1].asset.task_id == "text-whale_statue"
    jsonschema.validate(json.loads(recipe.model_dump_json(exclude_none=True)), _load_schema())


def test_synthesis_dedups_text_asset_colliding_with_photo_label():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(
        dominant_colors=["#2E8B57"],
        brightness=0.8,
        tags=["beach"],
        detected_objects=[DetectedObject(label="whale", bbox=[0.1, 0.1, 0.2, 0.4], prominence=0.35)],
    )
    photo_tasks = [MeshTask(task_id="task-whale", object_type="whale", provider="meshy")]
    text_assets = [ExtractedAsset(label="whale", prompt="a different whale prompt", density=0.9)]
    text_tasks = [MeshTask(task_id="text-whale", object_type="whale", provider="meshy")]

    recipe = service.synthesize(scene, "beach", photo_tasks, text_assets, text_tasks)

    # Only the photo-derived "whale" survives, with its own density/asset.
    assert [o.type for o in recipe.objects] == ["whale"]
    assert recipe.objects[0].density == 0.35
    assert recipe.objects[0].asset.task_id == "task-whale"


def test_synthesis_truncates_combined_objects_to_twelve():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(
        dominant_colors=["#2E8B57"],
        brightness=0.8,
        tags=["beach"],
        detected_objects=[
            DetectedObject(label=f"photo_{i}", bbox=[0.1, 0.1, 0.1, 0.1], prominence=0.5) for i in range(8)
        ],
    )
    text_assets = [ExtractedAsset(label=f"text_{i}", prompt="p", density=0.2) for i in range(8)]

    recipe = service.synthesize(scene, "beach", [], text_assets, [])

    assert len(recipe.objects) == 12
    # All 8 photo objects survive; only the first 4 text objects fit.
    assert [o.type for o in recipe.objects[:8]] == [f"photo_{i}" for i in range(8)]
    assert [o.type for o in recipe.objects[8:]] == [f"text_{i}" for i in range(4)]
    jsonschema.validate(json.loads(recipe.model_dump_json(exclude_none=True)), _load_schema())
