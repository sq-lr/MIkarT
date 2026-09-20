from __future__ import annotations

import json
from pathlib import Path

import jsonschema
import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError

from app.main import app
from app.models.world_recipe import WorldRecipe
from app.services.filler_asset_service import FillerAsset
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
        "sky": "sunny",
    },
    "track": {"width": 8.0, "length": 800.0, "difficulty": 0.5, "surface": "concrete"},
    "objects": [
        {"type": "palm_tree", "density": 0.5, "placement": "roadside", "asset": {"task_id": "0193a0c1-abcd", "provider": "meshy"}},
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


def test_placement_is_optional_defaults_to_scattered_and_is_validated():
    recipe = WorldRecipe.model_validate(VALID_RECIPE)
    assert recipe.objects[0].placement == "roadside"
    assert recipe.objects[1].placement == "scattered"  # absent in the input
    # We always emit it, and the schema accepts every value we can emit.
    dumped = json.loads(recipe.model_dump_json(exclude_none=True))
    assert dumped["objects"][1]["placement"] == "scattered"
    jsonschema.validate(dumped, _load_schema())

    data = json.loads(json.dumps(VALID_RECIPE))
    data["objects"][0]["placement"] = "floating"
    with pytest.raises(ValidationError):
        WorldRecipe.model_validate(data)
    with pytest.raises(jsonschema.ValidationError):
        jsonschema.validate(data, _load_schema())


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


def test_derived_seeds_fit_unity_int32():
    from app.models.world_recipe import MAX_SEED, derive_seed

    # "Tropical Paradise" used to hash to 2745161991 (> Int32.MaxValue) and
    # made Unity reject the whole recipe. Check that and a spread of inputs.
    assert derive_seed("Tropical Paradise", "tropical beach") <= MAX_SEED
    assert all(0 <= derive_seed(f"input-{i}") <= MAX_SEED for i in range(500))

    data = json.loads(json.dumps(VALID_RECIPE))
    data["seed"] = MAX_SEED + 1
    with pytest.raises(ValidationError):
        WorldRecipe.model_validate(data)
    with pytest.raises(jsonschema.ValidationError):
        jsonschema.validate(data, _load_schema())


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


def test_generate_world_endpoint_accepts_blank_description():
    client = TestClient(app)
    for data in ({}, {"description": ""}, {"description": "   "}):
        response = client.post("/generate-world", files={"image": ("test.png", make_png(), "image/png")}, data=data)
        assert response.status_code == 200, data
        jsonschema.validate(response.json()["world_recipe"], _load_schema())


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


def test_synthesis_preserves_selected_sky():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(dominant_colors=["#2E8B57"], brightness=0.8, tags=["beach"])

    recipe = service.synthesize(scene, "make it a beach paradise", sky="sunset")

    assert recipe.world.sky == "sunset"


def test_synthesis_ground_color_overrides_palette_ground_slot():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(dominant_colors=["#2E8B57"], brightness=0.8, tags=["beach"])

    recipe = service.synthesize(scene, "make it a beach paradise", ground_color="#2f5233")

    assert recipe.palette[1] == "#2f5233"


def test_synthesis_without_ground_color_keeps_the_profiles_own_palette():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(dominant_colors=["#2E8B57"], brightness=0.8, tags=["beach"])

    recipe = service.synthesize(scene, "make it a beach paradise")  # ground_color omitted

    assert recipe.palette[1] == "#F4D35E"  # the "tropical" profile's own palette[1], untouched


def test_synthesis_selects_track_surface_from_scene():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(
        dominant_colors=["#C2B280"],
        brightness=0.7,
        tags=["desert"],
        track_surface="dirt",
    )

    recipe = service.synthesize(scene, "desert race")

    assert recipe.track.surface == "dirt"


def test_track_surface_values_are_validated():
    data = json.loads(json.dumps(VALID_RECIPE))
    data["track"]["surface"] = "water"

    with pytest.raises(ValidationError):
        WorldRecipe.model_validate(data)


def test_synthesis_carries_mood_from_scene():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(
        dominant_colors=["#1A1A2E"],
        brightness=0.2,
        tags=["night", "graveyard"],
        mood="spooky",
    )

    recipe = service.synthesize(scene, "a haunted graveyard at midnight")

    assert recipe.world.mood == "spooky"


def test_mood_defaults_to_energetic_and_is_validated():
    data = json.loads(json.dumps(VALID_RECIPE))
    data["world"].pop("mood", None)
    assert WorldRecipe.model_validate(data).world.mood == "energetic"

    data["world"]["mood"] = "melancholic"
    with pytest.raises(ValidationError):
        WorldRecipe.model_validate(data)


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

    # Most prominent first (readability only), and a duplicate label
    # collapses into its most prominent occurrence.
    assert [(o.type, o.density) for o in recipe.objects] == [("rock", 0.9), ("lantern", 0.35)]
    assert recipe.objects[0].asset is None
    assert recipe.objects[1].asset is not None and recipe.objects[1].asset.task_id == "task-lantern"
    jsonschema.validate(json.loads(recipe.model_dump_json(exclude_none=True)), _load_schema())


def test_synthesis_passes_placement_through_and_allows_two_landmarks():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(
        dominant_colors=["#2E8B57"],
        brightness=0.8,
        tags=["harbour"],
        detected_objects=[
            DetectedObject(label="lamp_post", bbox=[0.1, 0.1, 0.1, 0.4], prominence=0.3, placement="roadside"),
            DetectedObject(label="lighthouse", bbox=[0.4, 0.0, 0.2, 0.9], prominence=0.6, placement="landmark"),
            DetectedObject(label="mountain", bbox=[0.0, 0.0, 1.0, 0.4], prominence=0.5, placement="background"),
            DetectedObject(label="statue", bbox=[0.7, 0.5, 0.2, 0.4], prominence=0.4, placement="landmark"),  # second landmark
            DetectedObject(label="flagpole", bbox=[0.6, 0.6, 0.1, 0.3], prominence=0.35, placement="landmark"),  # third landmark
            DetectedObject(label="crate", bbox=[0.8, 0.8, 0.1, 0.1], prominence=0.1),  # no hint -> scattered
        ],
    )

    recipe = service.synthesize(scene, "harbour at dusk")

    assert {o.type: o.placement for o in recipe.objects} == {
        "lighthouse": "landmark",  # the two most prominent landmarks win
        "statue": "landmark",
        "mountain": "background",
        "flagpole": "scattered",  # demoted: at most two landmarks per recipe
        "lamp_post": "roadside",
        "crate": "scattered",
    }
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


def test_synthesis_passes_through_text_asset_placement():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(dominant_colors=["#2E8B57"], brightness=0.8, tags=["beach"])
    text_assets = [
        ExtractedAsset(label="vending_machine", prompt="p", density=0.4, placement="roadside"),
        ExtractedAsset(label="dragon_statue", prompt="p", density=0.15, placement="landmark"),
    ]

    recipe = service.synthesize(scene, "beach", [], text_assets, [])

    placements = {o.type: o.placement for o in recipe.objects}
    assert placements["vending_machine"] == "roadside"
    assert placements["dragon_statue"] == "landmark"
    jsonschema.validate(json.loads(recipe.model_dump_json(exclude_none=True)), _load_schema())


def test_synthesis_allows_one_photo_and_one_text_landmark():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(
        dominant_colors=["#2E8B57"],
        brightness=0.8,
        tags=["beach"],
        detected_objects=[DetectedObject(label="lighthouse", bbox=[0.1, 0.1, 0.2, 0.4], prominence=0.9, placement="landmark")],
    )
    text_assets = [ExtractedAsset(label="dragon_statue", prompt="p", density=0.15, placement="landmark")]

    recipe = service.synthesize(scene, "beach", [], text_assets, [])

    # Combined total (1 photo + 1 text) is within the cap of two -- both kept.
    placements = {o.type: o.placement for o in recipe.objects}
    assert placements["lighthouse"] == "landmark"
    assert placements["dragon_statue"] == "landmark"
    jsonschema.validate(json.loads(recipe.model_dump_json(exclude_none=True)), _load_schema())


def test_synthesis_demotes_text_landmark_when_photo_already_has_two():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(
        dominant_colors=["#2E8B57"],
        brightness=0.8,
        tags=["beach"],
        detected_objects=[
            DetectedObject(label="lighthouse", bbox=[0.1, 0.1, 0.2, 0.4], prominence=0.9, placement="landmark"),
            DetectedObject(label="pier", bbox=[0.5, 0.5, 0.3, 0.3], prominence=0.7, placement="landmark"),
        ],
    )
    text_assets = [ExtractedAsset(label="dragon_statue", prompt="p", density=0.15, placement="landmark")]

    recipe = service.synthesize(scene, "beach", [], text_assets, [])

    placements = {o.type: o.placement for o in recipe.objects}
    assert placements["lighthouse"] == "landmark"
    assert placements["pier"] == "landmark"
    assert placements["dragon_statue"] == "scattered"  # demoted -- the cap of two is already used by the photo
    jsonschema.validate(json.loads(recipe.model_dump_json(exclude_none=True)), _load_schema())


def test_synthesis_appends_filler_assets_found_in_library():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(
        dominant_colors=["#2E8B57"],
        brightness=0.8,
        tags=["beach"],
        detected_objects=[DetectedObject(label="lantern", bbox=[0.1, 0.1, 0.2, 0.4], prominence=0.35)],
    )
    photo_tasks = [MeshTask(task_id="task-lantern", object_type="lantern", provider="meshy")]
    filler_assets = [FillerAsset(keyword="traffic_cone", density=0.6, placement="roadside")]
    filler_tasks = [MeshTask(task_id="poly-cone-1", object_type="traffic_cone", provider="polypizza")]

    recipe = service.synthesize(scene, "beach", photo_tasks, [], [], filler_assets, filler_tasks)

    assert [o.type for o in recipe.objects] == ["lantern", "traffic_cone"]
    cone = recipe.objects[1]
    assert cone.placement == "roadside"
    assert cone.asset.task_id == "poly-cone-1" and cone.asset.provider == "polypizza"
    jsonschema.validate(json.loads(recipe.model_dump_json(exclude_none=True)), _load_schema())


def test_synthesis_drops_filler_asset_with_no_library_match():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(dominant_colors=["#2E8B57"], brightness=0.8, tags=["beach"])
    # No matching MeshTask -- the keyword search found nothing.
    filler_assets = [FillerAsset(keyword="obscure_thing", density=0.5, placement="scattered")]

    recipe = service.synthesize(scene, "beach", [], [], [], filler_assets, [])

    assert "obscure_thing" not in [o.type for o in recipe.objects]


def test_synthesis_filler_asset_collides_with_existing_object_is_skipped():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(
        dominant_colors=["#2E8B57"],
        brightness=0.8,
        tags=["beach"],
        detected_objects=[DetectedObject(label="rock", bbox=[0.1, 0.1, 0.2, 0.4], prominence=0.5)],
    )
    photo_tasks = [MeshTask(task_id="task-rock", object_type="rock", provider="meshy")]
    filler_assets = [FillerAsset(keyword="rock", density=0.9, placement="scattered")]
    filler_tasks = [MeshTask(task_id="poly-rock-1", object_type="rock", provider="polypizza")]

    recipe = service.synthesize(scene, "beach", photo_tasks, [], [], filler_assets, filler_tasks)

    # Only the photo-derived "rock" survives, with its own Meshy asset.
    assert [o.type for o in recipe.objects] == ["rock"]
    assert recipe.objects[0].asset.provider == "meshy"


def test_synthesis_truncates_filler_before_photo_and_text():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(
        dominant_colors=["#2E8B57"],
        brightness=0.8,
        tags=["beach"],
        detected_objects=[
            DetectedObject(label=f"photo_{i}", bbox=[0.1, 0.1, 0.1, 0.1], prominence=0.5) for i in range(10)
        ],
    )
    text_assets = [ExtractedAsset(label="text_0", prompt="p", density=0.2)]
    filler_assets = [FillerAsset(keyword=f"filler_{i}", density=0.2, placement="scattered") for i in range(3)]
    filler_tasks = [
        MeshTask(task_id=f"poly-{i}", object_type=f"filler_{i}", provider="polypizza") for i in range(3)
    ]

    recipe = service.synthesize(scene, "beach", [], text_assets, [], filler_assets, filler_tasks)

    assert len(recipe.objects) == 12
    # 10 photo + 1 text = 11 objects; only 1 of the 3 filler objects fits.
    types = [o.type for o in recipe.objects]
    assert types[:10] == [f"photo_{i}" for i in range(10)]
    assert types[10] == "text_0"
    assert types[11] == "filler_0"


def test_synthesis_respects_configured_max_assets_below_schema_ceiling():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(
        dominant_colors=["#2E8B57"],
        brightness=0.8,
        tags=["beach"],
        detected_objects=[
            DetectedObject(label=f"photo_{i}", bbox=[0.1, 0.1, 0.1, 0.1], prominence=0.5) for i in range(5)
        ],
    )

    recipe = service.synthesize(scene, "beach", [], [], [], [], [], max_assets=3)

    assert len(recipe.objects) == 3
    jsonschema.validate(json.loads(recipe.model_dump_json(exclude_none=True)), _load_schema())


def test_synthesis_clamps_max_assets_to_schema_ceiling():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(
        dominant_colors=["#2E8B57"],
        brightness=0.8,
        tags=["beach"],
        detected_objects=[
            DetectedObject(label=f"photo_{i}", bbox=[0.1, 0.1, 0.1, 0.1], prominence=0.5) for i in range(20)
        ],
    )

    # A misconfigured MAX_ASSETS_PER_WORLD above the schema's max_length=12
    # must not produce an invalid recipe.
    recipe = service.synthesize(scene, "beach", [], [], [], [], [], max_assets=50)

    assert len(recipe.objects) == 12
    jsonschema.validate(json.loads(recipe.model_dump_json(exclude_none=True)), _load_schema())


def test_synthesis_personalize_false_ignores_photo_and_text_objects():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(
        dominant_colors=["#2E8B57"],
        brightness=0.8,
        tags=["beach"],
        detected_objects=[DetectedObject(label="lighthouse", bbox=[0.1, 0.1, 0.2, 0.4], prominence=0.9, placement="landmark")],
    )
    photo_tasks = [MeshTask(task_id="task-lighthouse", object_type="lighthouse", provider="meshy")]
    text_assets = [ExtractedAsset(label="dragon_statue", prompt="p", density=0.15)]
    text_tasks = [MeshTask(task_id="task-dragon", object_type="dragon_statue", provider="meshy")]
    filler_assets = [FillerAsset(keyword="park_bench", density=0.5, placement="roadside")]
    filler_tasks = [MeshTask(task_id="poly-bench", object_type="park_bench", provider="polypizza")]

    recipe = service.synthesize(
        scene, "beach", photo_tasks, text_assets, text_tasks, filler_assets, filler_tasks, personalize=False
    )

    # Photo and text objects are ignored entirely -- even though mesh tasks
    # were passed in (as if Meshy had actually been called), personalize=False
    # means the whole world comes from filler only.
    assert [o.type for o in recipe.objects] == ["park_bench"]
    jsonschema.validate(json.loads(recipe.model_dump_json(exclude_none=True)), _load_schema())


def test_synthesis_personalize_false_allows_filler_landmark():
    service = MockWorldSynthesisService()
    scene = SceneUnderstanding(dominant_colors=["#2E8B57"], brightness=0.8, tags=["beach"])
    filler_assets = [
        FillerAsset(keyword="lighthouse", density=0.15, placement="landmark"),
        FillerAsset(keyword="bench", density=0.5, placement="roadside"),
    ]
    filler_tasks = [
        MeshTask(task_id="poly-lighthouse", object_type="lighthouse", provider="polypizza"),
        MeshTask(task_id="poly-bench", object_type="bench", provider="polypizza"),
    ]

    recipe = service.synthesize(scene, "beach", [], [], [], filler_assets, filler_tasks, personalize=False)

    placements = {o.type: o.placement for o in recipe.objects}
    assert placements["lighthouse"] == "landmark"
    assert placements["bench"] == "roadside"
    jsonschema.validate(json.loads(recipe.model_dump_json(exclude_none=True)), _load_schema())
