from __future__ import annotations

import base64
import json
import threading

import httpx
import pytest

from app.services.mesh_generation_service import MeshyMeshGenerationService
from app.services.object_cropper import ObjectCrop
from app.services.text_asset_service import ExtractedAsset

GLB_BYTES = b"glTF\x02\x00\x00\x00fake"


class FakeMeshy:
    """Scripted stand-in for api.meshy.ai, driven through httpx.MockTransport."""

    def __init__(self):
        self.requests: list[httpx.Request] = []
        self.status = "PENDING"
        self.progress = 0
        self.glb_downloads = 0
        # Text-to-3D: preview always resolves to "preview-456" and, once
        # refine is submitted, to "refine-789" -- fixed ids keep the fake
        # simple since a real test only ever has one text asset in flight.
        self.preview_status = "PENDING"
        self.preview_progress = 0
        self.refine_status = "PENDING"
        self.refine_progress = 0
        self.refine_submissions = 0

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
        if request.method == "POST" and request.url.path == "/openapi/v2/text-to-3d":
            body = json.loads(request.content)
            if body["mode"] == "preview":
                return httpx.Response(202, json={"result": "preview-456"})
            if body["mode"] == "refine":
                assert body["preview_task_id"] == "preview-456"
                self.refine_submissions += 1
                return httpx.Response(202, json={"result": "refine-789"})
        if request.method == "GET" and request.url.path == "/openapi/v2/text-to-3d/preview-456":
            body = {"id": "preview-456", "type": "text-to-3d-preview", "status": self.preview_status, "progress": self.preview_progress}
            if self.preview_status == "FAILED":
                body["task_error"] = {"message": "prompt rejected"}
            return httpx.Response(200, json=body)
        if request.method == "GET" and request.url.path == "/openapi/v2/text-to-3d/refine-789":
            body = {"id": "refine-789", "type": "text-to-3d-refine", "status": self.refine_status, "progress": self.refine_progress}
            if self.refine_status == "SUCCEEDED":
                body["model_urls"] = {"glb": "https://cdn.example/model.glb"}
            if self.refine_status == "FAILED":
                body["task_error"] = {"message": "refine failed"}
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


def test_submit_text_sends_preview_request(meshy):
    fake, service = meshy
    asset = ExtractedAsset(label="whale_statue", prompt="a giant stone whale statue", density=0.2)

    task = service.submit_text(asset)

    assert task is not None
    assert (task.task_id, task.object_type, task.provider) == ("preview-456", "whale_statue", "meshy")
    request = fake.requests[-1]
    body = json.loads(request.content)
    assert body["mode"] == "preview"
    assert body["prompt"] == "a giant stone whale statue"
    assert body["geometry_resolution"] == "standard"


def test_get_status_preview_in_progress_reports_first_half_progress(meshy):
    fake, service = meshy
    service.submit_text(ExtractedAsset(label="whale_statue", prompt="p", density=0.2))

    fake.preview_progress = 40
    status = service.get_status("preview-456")

    assert status.status == "pending"
    assert status.progress == 20
    assert fake.refine_submissions == 0


def test_get_status_auto_submits_refine_on_preview_success(meshy):
    fake, service = meshy
    service.submit_text(ExtractedAsset(label="whale_statue", prompt="p", density=0.2))

    fake.preview_status = "SUCCEEDED"
    status = service.get_status("preview-456")

    assert status.status == "pending"
    assert status.progress == 50
    assert fake.refine_submissions == 1


def test_get_status_does_not_double_submit_refine(meshy):
    fake, service = meshy
    service.submit_text(ExtractedAsset(label="whale_statue", prompt="p", density=0.2))
    fake.preview_status = "SUCCEEDED"

    service.get_status("preview-456")
    service.get_status("preview-456")

    assert fake.refine_submissions == 1


def test_get_status_concurrent_calls_submit_refine_once(meshy):
    fake, service = meshy
    service.submit_text(ExtractedAsset(label="whale_statue", prompt="p", density=0.2))
    fake.preview_status = "SUCCEEDED"

    threads = [threading.Thread(target=service.get_status, args=("preview-456",)) for _ in range(10)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()

    assert fake.refine_submissions == 1


def test_get_status_refine_in_progress_reports_second_half_progress(meshy):
    fake, service = meshy
    service.submit_text(ExtractedAsset(label="whale_statue", prompt="p", density=0.2))
    fake.preview_status = "SUCCEEDED"
    service.get_status("preview-456")  # kicks off refine

    fake.refine_status = "IN_PROGRESS"
    fake.refine_progress = 60
    status = service.get_status("preview-456")

    assert status.status == "pending"
    assert status.progress == 80


def test_get_status_ready_only_after_refine_succeeds(meshy):
    fake, service = meshy
    service.submit_text(ExtractedAsset(label="whale_statue", prompt="p", density=0.2))
    fake.preview_status = "SUCCEEDED"
    service.get_status("preview-456")  # kicks off refine

    fake.refine_status = "SUCCEEDED"
    status = service.get_status("preview-456")

    assert status.status == "ready"
    assert status.progress == 100


def test_preview_failure_reported_before_any_refine_submitted(meshy):
    fake, service = meshy
    service.submit_text(ExtractedAsset(label="whale_statue", prompt="p", density=0.2))

    fake.preview_status = "FAILED"
    status = service.get_status("preview-456")

    assert status.status == "failed"
    assert status.error == "prompt rejected"
    assert fake.refine_submissions == 0


def test_fetch_model_text_asset_waits_for_refine_and_caches(meshy):
    fake, service = meshy
    service.submit_text(ExtractedAsset(label="whale_statue", prompt="p", density=0.2))

    # Refine hasn't even started yet.
    assert service.fetch_model("preview-456") is None

    fake.preview_status = "SUCCEEDED"
    service.get_status("preview-456")  # kicks off refine
    assert service.fetch_model("preview-456") is None  # refine still pending

    fake.refine_status = "SUCCEEDED"
    assert service.fetch_model("preview-456") == GLB_BYTES
    assert service.fetch_model("preview-456") == GLB_BYTES
    assert fake.glb_downloads == 1
