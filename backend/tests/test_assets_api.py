from __future__ import annotations

import jsonschema
import pytest
from fastapi.testclient import TestClient

from app.main import app
from app.services import providers as providers_module
from app.services.asset_merge_service import AssetMergeResult, AssetMergeService, MergeCandidate, MergeDecision, MockAssetMergeService
from app.services.library_asset_service import LibraryAssetService, MockLibraryAssetService
from app.services.filler_asset_service import FillerAsset, FillerAssetExtraction, FillerAssetService, MockFillerAssetService
from app.services.mesh_generation_service import MeshGenerationService, MeshTask, MeshTaskRegistry, MeshTaskStatus
from app.services.object_cropper import ObjectCrop
from app.services.text_asset_service import (
    ExtractedAsset,
    MockTextAssetService,
    TextAssetExtraction,
    TextAssetService,
)
from app.services.vision_service import MockVisionService
from app.services.world_synthesis_service import MockWorldSynthesisService
from tests.conftest import make_png
from tests.test_world_recipe import _load_schema

GLB_BYTES = b"glTF\x02\x00\x00\x00fake"


class FakeMeshService(MeshGenerationService):
    """Issues one task per crop or text asset; status is scripted per task_id."""

    def __init__(self):
        self.statuses: dict[str, MeshTaskStatus] = {}
        self.submitted: list[str] = []

    def submit(self, crop: ObjectCrop) -> MeshTask | None:
        task_id = f"task-{crop.label}"
        self.submitted.append(task_id)
        self.statuses[task_id] = MeshTaskStatus(status="pending", progress=10)
        return MeshTask(task_id=task_id, object_type=crop.label, provider="meshy")

    def submit_text(self, asset: ExtractedAsset) -> MeshTask | None:
        task_id = f"text-{asset.label}"
        self.submitted.append(task_id)
        self.statuses[task_id] = MeshTaskStatus(status="pending", progress=10)
        return MeshTask(task_id=task_id, object_type=asset.label, provider="meshy")

    def get_status(self, task_id: str) -> MeshTaskStatus:
        return self.statuses[task_id]

    def fetch_model(self, task_id: str) -> bytes | None:
        return GLB_BYTES if self.statuses[task_id].status == "ready" else None


class FakeTextAssetService(TextAssetService):
    """Returns a scripted extraction, or raises if constructed with an
    exception instance -- used to test the graceful-degradation path."""

    def __init__(self, result: TextAssetExtraction | Exception):
        self._result = result

    def extract(self, description: str) -> TextAssetExtraction:
        if isinstance(self._result, Exception):
            raise self._result
        return self._result


class FakeFillerAssetService(FillerAssetService):
    """Returns a scripted extraction and records every max_landmarks it was
    called with, so a test can assert the endpoint passed the right value."""

    def __init__(self, extraction: FillerAssetExtraction):
        self._extraction = extraction
        self.max_landmarks_calls: list[int] = []

    def suggest(self, image_bytes: bytes, description: str, max_landmarks: int = 0) -> FillerAssetExtraction:
        self.max_landmarks_calls.append(max_landmarks)
        return self._extraction


class FakeLibraryService(LibraryAssetService):
    """Issues one ready task per filler keyword -- retrieval is synchronous,
    so unlike FakeMeshService there's no separate pending state to script."""

    def __init__(self):
        self.submitted: list[str] = []

    def submit(self, asset: FillerAsset, exclude_ids: frozenset[str] = frozenset()) -> MeshTask | None:
        task_id = f"poly-{asset.keyword}"
        self.submitted.append(task_id)
        return MeshTask(task_id=task_id, object_type=asset.keyword, provider="polypizza")

    def get_status(self, task_id: str) -> MeshTaskStatus:
        return MeshTaskStatus(status="ready", progress=100)

    def fetch_model(self, task_id: str) -> bytes | None:
        return GLB_BYTES


@pytest.fixture
def fake_mesh():
    fake = FakeMeshService()
    providers_module.override(
        providers_module.Providers(
            vision=MockVisionService(),
            mesh=fake,
            synthesis=MockWorldSynthesisService(),
            text_assets=MockTextAssetService(),
            filler_assets=MockFillerAssetService(),
            asset_merge=MockAssetMergeService(),
            library=MockLibraryAssetService(),
            registry=MeshTaskRegistry(),
            max_objects_per_world=4,
            max_text_assets_per_world=2,
            max_filler_assets_per_world=2,
            max_assets_per_world=12,
        )
    )
    return fake


def test_generate_world_attaches_asset_handles_and_registers_tasks(fake_mesh):
    client = TestClient(app)
    response = client.post(
        "/generate-world",
        files={"image": ("test.png", make_png(), "image/png")},
        data={"description": "a forest", "personalize": "true"},
    )

    assert response.status_code == 200
    recipe = response.json()["world_recipe"]
    jsonschema.validate(recipe, _load_schema())
    assert fake_mesh.submitted
    registry = providers_module.get_providers().registry
    with_asset = [obj for obj in recipe["objects"] if "asset" in obj]
    # Exactly the objects whose crop was submitted carry a handle (a bbox too
    # small to crop yields no task and no asset).
    assert sorted(obj["asset"]["task_id"] for obj in with_asset) == sorted(fake_mesh.submitted)
    for obj in with_asset:
        assert obj["asset"] == {"task_id": f"task-{obj['type']}", "provider": "meshy"}
        assert registry.get(obj["asset"]["task_id"]) is not None


def test_asset_status_and_download_lifecycle(fake_mesh):
    client = TestClient(app)
    client.post(
        "/generate-world",
        files={"image": ("t.png", make_png(), "image/png")},
        data={"description": "x", "personalize": "true"},
    )
    task_id = fake_mesh.submitted[0]

    assert client.get(f"/assets/{task_id}").json() == {"status": "pending", "progress": 10}
    assert client.get(f"/assets/{task_id}/model.glb").status_code == 409

    fake_mesh.statuses[task_id] = MeshTaskStatus(status="ready", progress=100)
    assert client.get(f"/assets/{task_id}").json()["status"] == "ready"
    download = client.get(f"/assets/{task_id}/model.glb")
    assert download.status_code == 200
    assert download.headers["content-type"] == "model/gltf-binary"
    assert download.content == GLB_BYTES

    fake_mesh.statuses[task_id] = MeshTaskStatus(status="failed", error="boom")
    assert client.get(f"/assets/{task_id}").json() == {"status": "failed", "progress": 0, "error": "boom"}


def test_unknown_task_is_404(fake_mesh):
    client = TestClient(app)
    assert client.get("/assets/nope").status_code == 404
    assert client.get("/assets/nope/model.glb").status_code == 404


def test_submit_failure_leaves_object_without_asset(fake_mesh):
    def boom(crop):
        raise RuntimeError("meshy down")

    fake_mesh.submit = boom  # type: ignore[method-assign]
    client = TestClient(app)
    response = client.post(
        "/generate-world",
        files={"image": ("t.png", make_png(), "image/png")},
        data={"description": "x", "personalize": "true"},
    )

    assert response.status_code == 200
    assert all("asset" not in obj for obj in response.json()["world_recipe"]["objects"])


def test_generate_world_includes_text_extracted_assets_with_handles(fake_mesh):
    text_assets = FakeTextAssetService(
        TextAssetExtraction(key_assets=[ExtractedAsset(label="whale_statue", prompt="a giant stone whale", density=0.2)])
    )
    providers_module.override(
        providers_module.Providers(
            vision=MockVisionService(),
            mesh=fake_mesh,
            synthesis=MockWorldSynthesisService(),
            text_assets=text_assets,
            filler_assets=MockFillerAssetService(),
            asset_merge=MockAssetMergeService(),
            library=MockLibraryAssetService(),
            registry=MeshTaskRegistry(),
            max_objects_per_world=4,
            max_text_assets_per_world=2,
            max_filler_assets_per_world=2,
            max_assets_per_world=12,
        )
    )
    client = TestClient(app)
    response = client.post(
        "/generate-world",
        files={"image": ("t.png", make_png(), "image/png")},
        data={"description": "a forest", "personalize": "true"},
    )

    assert response.status_code == 200
    recipe = response.json()["world_recipe"]
    jsonschema.validate(recipe, _load_schema())
    whale = next(obj for obj in recipe["objects"] if obj["type"] == "whale_statue")
    assert whale["asset"] == {"task_id": "text-whale_statue", "provider": "meshy"}
    registry = providers_module.get_providers().registry
    assert registry.get("text-whale_statue") is not None


def test_text_extraction_failure_does_not_fail_world(fake_mesh):
    providers_module.override(
        providers_module.Providers(
            vision=MockVisionService(),
            mesh=fake_mesh,
            synthesis=MockWorldSynthesisService(),
            text_assets=FakeTextAssetService(RuntimeError("claude down")),
            filler_assets=MockFillerAssetService(),
            asset_merge=MockAssetMergeService(),
            library=MockLibraryAssetService(),
            registry=MeshTaskRegistry(),
            max_objects_per_world=4,
            max_text_assets_per_world=2,
            max_filler_assets_per_world=2,
            max_assets_per_world=12,
        )
    )
    client = TestClient(app)
    response = client.post(
        "/generate-world",
        files={"image": ("t.png", make_png(), "image/png")},
        data={"description": "a forest", "personalize": "true"},
    )

    assert response.status_code == 200
    jsonschema.validate(response.json()["world_recipe"], _load_schema())


def test_generate_world_personalize_defaults_to_false_and_skips_meshy(fake_mesh):
    filler = FakeFillerAssetService(
        FillerAssetExtraction(
            filler_assets=[
                FillerAsset(keyword="lighthouse", density=0.15, placement="landmark"),
                FillerAsset(keyword="bench", density=0.5, placement="roadside"),
            ]
        )
    )
    library = FakeLibraryService()
    # Would raise if ever called -- proves text extraction is skipped
    # entirely when not personalizing, not just its output discarded.
    text_assets = FakeTextAssetService(RuntimeError("text extraction should never run when personalize=False"))
    providers_module.override(
        providers_module.Providers(
            vision=MockVisionService(),
            mesh=fake_mesh,
            synthesis=MockWorldSynthesisService(),
            text_assets=text_assets,
            filler_assets=filler,
            asset_merge=MockAssetMergeService(),
            library=library,
            registry=MeshTaskRegistry(),
            max_objects_per_world=4,
            max_text_assets_per_world=2,
            max_filler_assets_per_world=2,
            max_assets_per_world=12,
        )
    )
    client = TestClient(app)
    # personalize omitted entirely -- defaults to False, matching the upload
    # screen's "Generate personalized assets" toggle default.
    response = client.post(
        "/generate-world",
        files={"image": ("t.png", make_png(), "image/png")},
        data={"description": "a fantasy kingdom"},
    )

    assert response.status_code == 200
    recipe = response.json()["world_recipe"]
    jsonschema.validate(recipe, _load_schema())

    # No Meshy calls at all: personalize=False skips crop_objects and text
    # extraction entirely, so there's nothing for the mesh provider to do.
    assert fake_mesh.submitted == []
    # Filler was allowed MAX_LANDMARKS (2) since nothing else can supply one.
    assert filler.max_landmarks_calls == [2]

    placements = {obj["type"]: obj["placement"] for obj in recipe["objects"]}
    assert placements["lighthouse"] == "landmark"
    assert placements["bench"] == "roadside"
    for obj in recipe["objects"]:
        assert obj["asset"]["provider"] == "polypizza"


class _RecordingLibraryService(LibraryAssetService):
    """Records the exclude_ids each submit() call received, so a test can
    confirm the second background-variant call actually excludes the first
    match instead of just repeating it."""

    def __init__(self):
        self.calls: list[frozenset[str]] = []

    def submit(self, asset: FillerAsset, exclude_ids: frozenset[str] = frozenset()) -> MeshTask | None:
        self.calls.append(exclude_ids)
        task_id = f"poly-{asset.keyword}-{len(self.calls)}"
        return MeshTask(task_id=task_id, object_type=asset.keyword, provider="polypizza")

    def get_status(self, task_id):
        return MeshTaskStatus(status="ready", progress=100)

    def fetch_model(self, task_id):
        return GLB_BYTES


def test_submit_filler_assets_gets_a_second_variant_for_background():
    from app.api.generate_world import _submit_filler_assets

    library = _RecordingLibraryService()
    assets = [
        FillerAsset(keyword="mountain", density=0.2, placement="background"),
        FillerAsset(keyword="bench", density=0.5, placement="roadside"),
    ]

    result_assets, result_tasks = _submit_filler_assets(library, assets)

    # background got a second, excluded-first-match submission; roadside didn't.
    assert [a.keyword for a in result_assets] == ["mountain", "mountain_2", "bench"]
    assert [t.object_type for t in result_tasks] == ["mountain", "mountain_2", "bench"]
    assert all(t.provider == "polypizza" for t in result_tasks)
    assert library.calls[0] == frozenset()  # first mountain search: nothing to exclude yet
    assert library.calls[1] == frozenset({result_tasks[0].task_id})  # second excludes the first match
    assert library.calls[2] == frozenset()  # bench: no variant search at all


def test_submit_filler_assets_skips_variant_when_no_second_match():
    from app.api.generate_world import _submit_filler_assets

    class NoSecondMatchLibrary(LibraryAssetService):
        def submit(self, asset, exclude_ids=frozenset()):
            if exclude_ids:
                return None  # only one match exists in this fake catalog
            return MeshTask(task_id="only-match", object_type=asset.keyword, provider="polypizza")

        def get_status(self, task_id):
            return MeshTaskStatus(status="ready", progress=100)

        def fetch_model(self, task_id):
            return GLB_BYTES

    assets = [FillerAsset(keyword="mountain", density=0.2, placement="background")]
    result_assets, result_tasks = _submit_filler_assets(NoSecondMatchLibrary(), assets)

    assert [a.keyword for a in result_assets] == ["mountain"]
    assert [t.task_id for t in result_tasks] == ["only-match"]


class FakeAssetMergeService(AssetMergeService):
    """Records every candidate list it was called with, and demotes/drops
    exactly what `decide` says by index -- lets a test script a specific
    merge outcome without touching the real Claude call."""

    def __init__(self, decide):
        self.decide = decide
        self.calls: list[list[MergeCandidate]] = []

    def merge(self, candidates: list[MergeCandidate], max_assets: int) -> AssetMergeResult:
        self.calls.append(candidates)
        return AssetMergeResult(decisions=[self.decide(c) for c in candidates])


def test_merge_assets_applies_decisions_and_never_touches_unrelated_fields():
    from app.services.filler_asset_service import FillerAsset
    from app.services.text_asset_service import ExtractedAsset
    from app.services.vision_service import DetectedObject
    from app.api.generate_world import _merge_assets

    photo = [DetectedObject(label="mountain", bbox=[0, 0, 1, 1], prominence=0.5, placement="background")]
    text = [ExtractedAsset(label="dragon_statue", prompt="a stone dragon", density=0.1, placement="landmark")]
    filler = [
        FillerAsset(keyword="cliff", density=0.2, placement="background"),
        FillerAsset(keyword="crate", density=0.3, placement="scattered"),
        FillerAsset(keyword="bench", density=0.5, placement="roadside"),
    ]

    def decide(candidate: MergeCandidate) -> MergeDecision:
        if candidate.label == "crate":
            return MergeDecision(index=candidate.index, keep=False, placement=candidate.placement)
        return MergeDecision(index=candidate.index, keep=True, placement=candidate.placement)

    merge_service = FakeAssetMergeService(decide)
    new_photo, new_text, new_filler = _merge_assets(merge_service, photo, text, filler, max_assets=12)

    # Photo's "background" tag is demoted to "scattered" before the merge
    # ever runs -- background is hardcoded to filler's own candidate, so
    # photo/text are never eligible for it.
    assert [(o.label, o.placement) for o in new_photo] == [("mountain", "scattered")]
    assert new_photo[0].bbox == [0, 0, 1, 1]  # untouched field survives the merge
    assert [o.label for o in new_text] == ["dragon_statue"]
    assert new_text[0].prompt == "a stone dragon"  # untouched field survives the merge
    # "cliff" (filler's background candidate) is always kept, unconditionally,
    # and never even reaches the merge call; "crate" was dropped by the
    # merge; "bench" was kept by the merge.
    assert [(o.keyword, o.placement) for o in new_filler] == [("cliff", "background"), ("bench", "roadside")]

    call = merge_service.calls[0]
    # "cliff" never appears here: it's excluded from the candidate pool.
    assert [(c.source, c.label, c.placement) for c in call] == [
        ("photo", "mountain", "scattered"),
        ("text", "dragon_statue", "landmark"),
        ("filler", "crate", "scattered"),
        ("filler", "bench", "roadside"),
    ]


def test_merge_assets_falls_back_unchanged_on_merge_failure():
    from app.services.filler_asset_service import FillerAsset
    from app.services.vision_service import DetectedObject
    from app.api.generate_world import _merge_assets

    photo = [DetectedObject(label="mountain", bbox=[0, 0, 1, 1], prominence=0.5, placement="background")]
    filler = [FillerAsset(keyword="cliff", density=0.2, placement="background")]

    class BoomMergeService(AssetMergeService):
        def merge(self, candidates, max_assets):
            raise RuntimeError("claude down")

    new_photo, new_text, new_filler = _merge_assets(BoomMergeService(), photo, [], filler, max_assets=12)

    # The background hardcoding (demote photo/text, always keep filler's) is
    # unconditional -- applied before the merge call is even attempted -- so
    # it still holds even when the merge call itself fails.
    assert [(o.label, o.placement) for o in new_photo] == [("mountain", "scattered")]
    assert new_text == []
    assert [(o.keyword, o.placement) for o in new_filler] == [("cliff", "background")]


def test_merge_assets_hardcodes_background_to_filler_even_when_photo_also_claims_it():
    from app.services.filler_asset_service import FillerAsset
    from app.services.vision_service import DetectedObject
    from app.api.generate_world import _merge_assets

    # Photo also detected something it thinks is "background" -- the merge
    # should never get a say in this: filler's candidate always wins, and
    # photo's is demoted before the merge call ever sees a candidate list.
    photo = [DetectedObject(label="mountain_range", bbox=[0, 0, 1, 1], prominence=0.6, placement="background")]
    filler = [FillerAsset(keyword="cliff", density=0.2, placement="background")]

    merge_service = FakeAssetMergeService(lambda c: MergeDecision(index=c.index, keep=True, placement=c.placement))
    new_photo, new_text, new_filler = _merge_assets(merge_service, photo, [], filler, max_assets=12)

    assert [(o.label, o.placement) for o in new_photo] == [("mountain_range", "scattered")]
    assert [(o.keyword, o.placement) for o in new_filler] == [("cliff", "background")]
    # "cliff" was never even offered to the merge call.
    assert all(c.label != "cliff" for c in merge_service.calls[0])


def test_merge_assets_skips_the_call_with_no_candidates():
    from app.api.generate_world import _merge_assets

    merge_service = FakeAssetMergeService(lambda c: MergeDecision(index=c.index, keep=True, placement=c.placement))
    result = _merge_assets(merge_service, [], [], [], max_assets=12)

    assert result == ([], [], [])
    assert merge_service.calls == []


def test_generate_world_personalize_true_runs_asset_merge_before_submission(fake_mesh):
    from app.services.vision_service import MockVisionService

    text_assets = FakeTextAssetService(
        TextAssetExtraction(key_assets=[ExtractedAsset(label="whale_statue", prompt="a giant stone whale", density=0.2)])
    )
    filler = FakeFillerAssetService(
        FillerAssetExtraction(
            filler_assets=[
                FillerAsset(keyword="cliff", density=0.2, placement="background"),
                FillerAsset(keyword="crate", density=0.3, placement="scattered"),
            ]
        )
    )
    library = FakeLibraryService()

    def decide(candidate: MergeCandidate) -> MergeDecision:
        # Drop the (non-background) filler candidate entirely -- proves a
        # merge-rejected candidate never reaches mesh/library submission.
        if candidate.label == "crate":
            return MergeDecision(index=candidate.index, keep=False, placement=candidate.placement)
        return MergeDecision(index=candidate.index, keep=True, placement=candidate.placement)

    merge_service = FakeAssetMergeService(decide)
    providers_module.override(
        providers_module.Providers(
            vision=MockVisionService(),
            mesh=fake_mesh,
            synthesis=MockWorldSynthesisService(),
            text_assets=text_assets,
            filler_assets=filler,
            asset_merge=merge_service,
            library=library,
            registry=MeshTaskRegistry(),
            max_objects_per_world=4,
            max_text_assets_per_world=2,
            max_filler_assets_per_world=2,
            max_assets_per_world=12,
        )
    )
    client = TestClient(app)
    response = client.post(
        "/generate-world",
        files={"image": ("t.png", make_png(), "image/png")},
        data={"description": "a forest", "personalize": "true"},
    )

    assert response.status_code == 200
    recipe = response.json()["world_recipe"]
    jsonschema.validate(recipe, _load_schema())
    assert len(merge_service.calls) == 1
    # "cliff" is filler's background candidate: hardcoded to always survive,
    # never even sent to the merge call, so it's still in the final recipe.
    assert "cliff" in [obj["type"] for obj in recipe["objects"]]
    # "crate" is a real merge decision (rejected) -- proves rejection still
    # prevents submission for a non-background candidate.
    assert "crate" not in [obj["type"] for obj in recipe["objects"]]
    # "cliff" is submitted twice (base + a second wall-variant search, since
    # it's a "background" keyword -- see _submit_filler_assets); "crate"
    # never reaches the library at all since the merge dropped it first.
    assert library.submitted == ["poly-cliff", "poly-cliff"]


def test_generate_world_personalize_false_never_runs_asset_merge():
    from app.services.vision_service import MockVisionService

    def decide(candidate):
        raise AssertionError("asset merge should never run when personalize=False")

    merge_service = FakeAssetMergeService(decide)
    filler = FakeFillerAssetService(
        FillerAssetExtraction(filler_assets=[FillerAsset(keyword="bench", density=0.5, placement="roadside")])
    )
    providers_module.override(
        providers_module.Providers(
            vision=MockVisionService(),
            mesh=FakeMeshService(),
            synthesis=MockWorldSynthesisService(),
            text_assets=MockTextAssetService(),
            filler_assets=filler,
            asset_merge=merge_service,
            library=FakeLibraryService(),
            registry=MeshTaskRegistry(),
            max_objects_per_world=4,
            max_text_assets_per_world=2,
            max_filler_assets_per_world=2,
            max_assets_per_world=12,
        )
    )
    client = TestClient(app)
    response = client.post(
        "/generate-world",
        files={"image": ("t.png", make_png(), "image/png")},
        data={"description": "a forest"},
    )

    assert response.status_code == 200
    assert merge_service.calls == []
