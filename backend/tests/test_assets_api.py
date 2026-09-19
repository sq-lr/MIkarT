from __future__ import annotations

import jsonschema
import pytest
from fastapi.testclient import TestClient

from app.main import app
from app.services import providers as providers_module
from app.services.mesh_generation_service import MeshGenerationService, MeshTask, MeshTaskRegistry, MeshTaskStatus
from app.services.object_cropper import ObjectCrop
from app.services.vision_service import MockVisionService
from app.services.world_synthesis_service import MockWorldSynthesisService
from tests.conftest import make_png
from tests.test_world_recipe import _load_schema

GLB_BYTES = b"glTF\x02\x00\x00\x00fake"


class FakeMeshService(MeshGenerationService):
    """Issues one task per crop; status is scripted per task_id."""

    def __init__(self):
        self.statuses: dict[str, MeshTaskStatus] = {}
        self.submitted: list[str] = []

    def submit(self, crop: ObjectCrop) -> MeshTask | None:
        task_id = f"task-{crop.label}"
        self.submitted.append(task_id)
        self.statuses[task_id] = MeshTaskStatus(status="pending", progress=10)
        return MeshTask(task_id=task_id, object_type=crop.label, provider="meshy")

    def get_status(self, task_id: str) -> MeshTaskStatus:
        return self.statuses[task_id]

    def fetch_model(self, task_id: str) -> bytes | None:
        return GLB_BYTES if self.statuses[task_id].status == "ready" else None


@pytest.fixture
def fake_mesh():
    fake = FakeMeshService()
    providers_module.override(
        providers_module.Providers(
            vision=MockVisionService(),
            mesh=fake,
            synthesis=MockWorldSynthesisService(),
            registry=MeshTaskRegistry(),
            max_objects_per_world=4,
        )
    )
    return fake


def test_generate_world_attaches_asset_handles_and_registers_tasks(fake_mesh):
    client = TestClient(app)
    response = client.post(
        "/generate-world",
        files={"image": ("test.png", make_png(), "image/png")},
        data={"description": "a forest"},
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
    client.post("/generate-world", files={"image": ("t.png", make_png(), "image/png")}, data={"description": "x"})
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
    response = client.post("/generate-world", files={"image": ("t.png", make_png(), "image/png")}, data={"description": "x"})

    assert response.status_code == 200
    assert all("asset" not in obj for obj in response.json()["world_recipe"]["objects"])
