"""Object crop -> 3D mesh, generated asynchronously by an external provider.

Replaces the earlier "asset retrieval" idea: instead of looking up a
pre-made asset for an object type, each object the VLM extracted from the
photo is turned into its own mesh. Generation takes minutes, so this is a
submit / poll / fetch interface -- Unity races on placeholders and swaps the
mesh in when it arrives (see docs/decisions/0006-meshy-async-mesh-generation.md).

Two implementations ship:
- MockMeshGenerationService (default): issues no tasks at all, so recipes
  carry no `asset` and Unity keeps its primitives. Needs no key or network.
- MeshyMeshGenerationService (MESH_PROVIDER=meshy): Meshy Image-to-3D.
"""

from __future__ import annotations

import base64
import logging
import threading
from abc import ABC, abstractmethod
from typing import Literal

import httpx
from pydantic import BaseModel

from app.services.object_cropper import ObjectCrop

logger = logging.getLogger(__name__)

MeshStatus = Literal["pending", "ready", "failed"]


class MeshTask(BaseModel):
    task_id: str
    object_type: str
    provider: str


class MeshTaskStatus(BaseModel):
    status: MeshStatus
    progress: int = 0
    error: str | None = None


class MeshGenerationService(ABC):
    @abstractmethod
    def submit(self, crop: ObjectCrop) -> MeshTask | None:
        """Start generating a mesh for one crop. Returns None when this
        provider doesn't generate meshes (mock) so the object simply has no
        asset; raises on a real provider error."""
        raise NotImplementedError

    @abstractmethod
    def get_status(self, task_id: str) -> MeshTaskStatus:
        raise NotImplementedError

    @abstractmethod
    def fetch_model(self, task_id: str) -> bytes | None:
        """GLB bytes once the task is ready, else None."""
        raise NotImplementedError


class MockMeshGenerationService(MeshGenerationService):
    """Bootstrap/offline default: no meshes are generated, every object keeps
    its placeholder primitive in Unity."""

    def submit(self, crop: ObjectCrop) -> MeshTask | None:
        return None

    def get_status(self, task_id: str) -> MeshTaskStatus:
        return MeshTaskStatus(status="failed", error="mock provider generates no meshes")

    def fetch_model(self, task_id: str) -> bytes | None:
        return None


MESHY_BASE_URL = "https://api.meshy.ai/openapi/v1"

# Meshy task status -> our provider-neutral status.
_MESHY_STATUS_MAP: dict[str, MeshStatus] = {
    "PENDING": "pending",
    "IN_PROGRESS": "pending",
    "SUCCEEDED": "ready",
    "FAILED": "failed",
    "CANCELED": "failed",
}


class MeshyMeshGenerationService(MeshGenerationService):
    """Meshy Image-to-3D (https://docs.meshy.ai/en/api/image-to-3d).

    submit  -> POST /image-to-3d with the crop as a base64 data URI
    status  -> GET  /image-to-3d/{id}
    fetch   -> download model_urls.glb, cached in-process so Unity's download
               and any retry don't re-hit Meshy's (expiring) signed URL.
    """

    def __init__(
        self,
        api_key: str,
        model_type: str = "lowpoly",
        should_texture: bool = True,
        base_url: str = MESHY_BASE_URL,
        transport: httpx.BaseTransport | None = None,
        timeout: float = 30.0,
    ):
        if not api_key:
            raise ValueError("MESHY_API_KEY is required for MESH_PROVIDER=meshy")
        self._model_type = model_type
        self._should_texture = should_texture
        self._client = httpx.Client(
            base_url=base_url,
            headers={"Authorization": f"Bearer {api_key}"},
            timeout=timeout,
            transport=transport,
        )
        self._model_cache: dict[str, bytes] = {}
        self._lock = threading.Lock()

    def submit(self, crop: ObjectCrop) -> MeshTask | None:
        data_uri = "data:image/png;base64," + base64.standard_b64encode(crop.png_bytes).decode("ascii")
        response = self._client.post(
            "/image-to-3d",
            json={
                "image_url": data_uri,
                "model_type": self._model_type,
                "should_texture": self._should_texture,
                "ai_model": "latest",
            },
        )
        response.raise_for_status()
        task_id = response.json()["result"]
        logger.info("meshy: submitted %s -> task %s", crop.label, task_id)
        return MeshTask(task_id=task_id, object_type=crop.label, provider="meshy")

    def get_status(self, task_id: str) -> MeshTaskStatus:
        response = self._client.get(f"/image-to-3d/{task_id}")
        response.raise_for_status()
        body = response.json()
        meshy_status = body.get("status", "")
        status = _MESHY_STATUS_MAP.get(meshy_status)
        if status is None:
            logger.warning("meshy: unknown status %r for task %s", meshy_status, task_id)
            status = "pending"
        error = None
        if status == "failed":
            error = (body.get("task_error") or {}).get("message") or meshy_status
        return MeshTaskStatus(status=status, progress=int(body.get("progress", 0)), error=error)

    def fetch_model(self, task_id: str) -> bytes | None:
        with self._lock:
            cached = self._model_cache.get(task_id)
        if cached is not None:
            return cached

        response = self._client.get(f"/image-to-3d/{task_id}")
        response.raise_for_status()
        body = response.json()
        if body.get("status") != "SUCCEEDED":
            return None
        glb_url = (body.get("model_urls") or {}).get("glb")
        if not glb_url:
            logger.warning("meshy: task %s succeeded but has no glb url", task_id)
            return None

        # Signed CDN URL, absolute -- bypasses the client's base_url.
        download = self._client.get(glb_url, follow_redirects=True)
        download.raise_for_status()
        glb = download.content
        with self._lock:
            self._model_cache[task_id] = glb
        return glb


class MeshTaskRegistry:
    """In-process map of task_id -> MeshTask for every mesh this backend has
    submitted, so GET /assets/{task_id} can reject unknown IDs and report the
    object type. Process-lifetime only: this is a local single-session game,
    and a restarted backend means a new world anyway."""

    def __init__(self) -> None:
        self._tasks: dict[str, MeshTask] = {}
        self._lock = threading.Lock()

    def add(self, task: MeshTask) -> None:
        with self._lock:
            self._tasks[task.task_id] = task

    def get(self, task_id: str) -> MeshTask | None:
        with self._lock:
            return self._tasks.get(task_id)

    def clear(self) -> None:
        with self._lock:
            self._tasks.clear()
