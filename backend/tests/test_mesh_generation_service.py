from __future__ import annotations

import base64
import json

import httpx
import pytest

from app.services.mesh_generation_service import MeshyMeshGenerationService
from app.services.object_cropper import ObjectCrop

GLB_BYTES = b"glTF\x02\x00\x00\x00fake"


class FakeMeshy:
    """Scripted stand-in for api.meshy.ai, driven through httpx.MockTransport."""

    def __init__(self):
        self.requests: list[httpx.Request] = []
        self.status = "PENDING"
        self.progress = 0
        self.glb_downloads = 0

    def handler(self, request: httpx.Request) -> httpx.Response:
        self.requests.append(request)
        if request.url.host == "cdn.example" and request.url.path == "/model.glb":
            self.glb_downloads += 1
            return httpx.Response(200, content=GLB_BYTES)
        if request.method == "POST" and request.url.path == "/openapi/v1/image-to-3d":
            return httpx.Response(202, json={"result": "task-123"})
        if request.method == "GET" and request.url.path == "/openapi/v1/image-to-3d/task-123":
            body = {"id": "task-123", "status": self.status, "progress": self.progress}
            if self.status == "SUCCEEDED":
                body["model_urls"] = {"glb": "https://cdn.example/model.glb", "fbx": "https://cdn.example/model.fbx"}
            if self.status == "FAILED":
                body["task_error"] = {"message": "input image rejected"}
            return httpx.Response(200, json=body)
        return httpx.Response(404, json={"message": "not found"})


@pytest.fixture
def meshy():
    fake = FakeMeshy()
    service = MeshyMeshGenerationService(api_key="secret", transport=httpx.MockTransport(fake.handler))
    return fake, service


def test_requires_api_key():
    with pytest.raises(ValueError):
        MeshyMeshGenerationService(api_key="")


def test_submit_sends_data_uri_with_bearer_auth(meshy):
    fake, service = meshy
    crop = ObjectCrop(label="palm_tree", png_bytes=b"\x89PNGfake", bbox=(0, 0, 10, 10))

    task = service.submit(crop)

    assert task is not None
    assert (task.task_id, task.object_type, task.provider) == ("task-123", "palm_tree", "meshy")
    request = fake.requests[-1]
    assert request.headers["authorization"] == "Bearer secret"
    body = json.loads(request.content)
    assert body["image_url"] == "data:image/png;base64," + base64.standard_b64encode(b"\x89PNGfake").decode()
    assert body["model_type"] == "lowpoly" and body["should_texture"] is True


def test_status_mapping(meshy):
    fake, service = meshy
    for meshy_status, expected in [("PENDING", "pending"), ("IN_PROGRESS", "pending"), ("SUCCEEDED", "ready"), ("CANCELED", "failed")]:
        fake.status = meshy_status
        assert service.get_status("task-123").status == expected

    fake.status = "FAILED"
    status = service.get_status("task-123")
    assert status.status == "failed" and status.error == "input image rejected"


def test_fetch_model_waits_for_success_and_caches(meshy):
    fake, service = meshy

    fake.status = "IN_PROGRESS"
    assert service.fetch_model("task-123") is None

    fake.status = "SUCCEEDED"
    assert service.fetch_model("task-123") == GLB_BYTES
    assert service.fetch_model("task-123") == GLB_BYTES
    assert fake.glb_downloads == 1
