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
from app.services.text_asset_service import ExtractedAsset

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
    def submit_text(self, asset: ExtractedAsset) -> MeshTask | None:
        """Start generating a mesh from a text prompt alone (no photo crop).
        Returns immediately with a single public task_id that stays valid for
        the object's whole lifecycle, even for a provider whose real API is a
        multi-phase workflow underneath (see MeshyMeshGenerationService).
        Returns None when this provider doesn't generate meshes (mock);
        raises on a real provider error."""
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

    def submit_text(self, asset: ExtractedAsset) -> MeshTask | None:
        return None

    def get_status(self, task_id: str) -> MeshTaskStatus:
        return MeshTaskStatus(status="failed", error="mock provider generates no meshes")

    def fetch_model(self, task_id: str) -> bytes | None:
        return None


MESHY_BASE_URL = "https://api.meshy.ai/openapi/v1"

# Text-to-3D lives on a different API version than image-to-3d. Passed as an
# absolute URL to self._client.post/get, which bypasses the client's v1
# base_url -- the same trick fetch_model() already uses for Meshy's signed
# CDN download URLs.
MESHY_TEXT_TO_3D_URL = "https://api.meshy.ai/openapi/v2/text-to-3d"

# Pinned so a future Meshy release can't silently change the look of
# generated worlds. "meshy-7" is deprecated. Note that 7.x rejects
# model_type=lowpoly ("not supported for meshy-7"); low poly counts come from
# should_remesh + target_polycount instead.
MESHY_AI_MODEL = "meshy-7.1"

# Meshy task status -> our provider-neutral status.
_MESHY_STATUS_MAP: dict[str, MeshStatus] = {
    "PENDING": "pending",
    "IN_PROGRESS": "pending",
    "SUCCEEDED": "ready",
    "FAILED": "failed",
    "CANCELED": "failed",
}


class _TextTaskState:
    """Tracks one text-to-3d asset's progress through Meshy's two-phase
    preview -> refine workflow. `refine_task_id` is None until the preview
    succeeds and refine has been kicked off; `refine_lock` guards that
    check-then-set so two concurrent pollers can't both submit refine."""

    __slots__ = ("refine_task_id", "refine_lock")

    def __init__(self) -> None:
        self.refine_task_id: str | None = None
        self.refine_lock = threading.Lock()


class MeshyMeshGenerationService(MeshGenerationService):
    """Meshy Image-to-3D (https://docs.meshy.ai/en/api/image-to-3d) and
    Text-to-3D (https://docs.meshy.ai/en/api/text-to-3d).

    Image-to-3D:
        submit  -> POST /image-to-3d with the crop as a base64 data URI
        status  -> GET  /image-to-3d/{id}
        fetch   -> download model_urls.glb

    Text-to-3D is a two-phase workflow (preview: untextured geometry, then
    refine: adds texture, referencing the preview's result) -- submit_text
    kicks off only the preview and returns its task_id as the single public
    id for the asset's whole lifecycle; get_status/fetch_model transparently
    detect "preview succeeded" and submit refine themselves, so callers never
    see the two phases (see docs/decisions/0007-text-to-3d-key-assets.md).

    Both endpoints share one in-process GLB cache so Unity's download and any
    retry don't re-hit Meshy's (expiring) signed URL.
    """

    def __init__(
        self,
        api_key: str,
        model_type: str = "standard",
        should_texture: bool = True,
        target_polycount: int = 8000,
        base_url: str = MESHY_BASE_URL,
        text_geometry_resolution: str = "standard",
        text_texture_resolution: str = "2k",
        text_enable_pbr: bool = False,
        transport: httpx.BaseTransport | None = None,
        timeout: float = 30.0,
    ):
        if not api_key:
            raise ValueError("MESHY_API_KEY is required for MESH_PROVIDER=meshy")
        self._model_type = model_type
        self._should_texture = should_texture
        self._target_polycount = target_polycount
        self._text_geometry_resolution = text_geometry_resolution
        self._text_texture_resolution = text_texture_resolution
        self._text_enable_pbr = text_enable_pbr
        self._client = httpx.Client(
            base_url=base_url,
            headers={"Authorization": f"Bearer {api_key}"},
            timeout=timeout,
            transport=transport,
        )
        self._model_cache: dict[str, bytes] = {}
        self._lock = threading.Lock()
        # task_id (the preview task's id, our public id) -> its phase state.
        # Only ever guards dict read/insert, never a network call, so
        # concurrent get_status calls for *different* task_ids never contend.
        self._text_tasks: dict[str, _TextTaskState] = {}
        self._text_tasks_lock = threading.Lock()

    def submit(self, crop: ObjectCrop) -> MeshTask | None:
        data_uri = "data:image/png;base64," + base64.standard_b64encode(crop.png_bytes).decode("ascii")
        response = self._client.post(
            "/image-to-3d",
            json={
                "image_url": data_uri,
                "model_type": self._model_type,
                "should_texture": self._should_texture,
                "ai_model": MESHY_AI_MODEL,
                "should_remesh": True,
                "target_polycount": self._target_polycount,
            },
        )
        response.raise_for_status()
        task_id = response.json()["result"]
        logger.info("meshy: submitted %s -> task %s", crop.label, task_id)
        return MeshTask(task_id=task_id, object_type=crop.label, provider="meshy")

    def submit_text(self, asset: ExtractedAsset) -> MeshTask | None:
        response = self._client.post(
            MESHY_TEXT_TO_3D_URL,
            json={
                "mode": "preview",
                "prompt": asset.prompt,
                "ai_model": MESHY_AI_MODEL,
                "geometry_resolution": self._text_geometry_resolution,
            },
        )
        response.raise_for_status()
        preview_task_id = response.json()["result"]
        with self._text_tasks_lock:
            self._text_tasks[preview_task_id] = _TextTaskState()
        logger.info("meshy: submitted text asset %s -> preview task %s", asset.label, preview_task_id)
        return MeshTask(task_id=preview_task_id, object_type=asset.label, provider="meshy")

    def get_status(self, task_id: str) -> MeshTaskStatus:
        with self._text_tasks_lock:
            state = self._text_tasks.get(task_id)
        if state is not None:
            return self._get_text_status(task_id, state)

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

        with self._text_tasks_lock:
            state = self._text_tasks.get(task_id)
        if state is not None:
            with state.refine_lock:
                refine_id = state.refine_task_id
            if refine_id is None:
                return None  # refine hasn't started (or succeeded) yet
            body = self._get_text_to_3d(refine_id)
        else:
            response = self._client.get(f"/image-to-3d/{task_id}")
            response.raise_for_status()
            body = response.json()

        return self._extract_and_cache_glb(task_id, body)

    # -- Text-to-3D helpers -------------------------------------------------

    def _get_text_to_3d(self, task_id: str) -> dict:
        response = self._client.get(f"{MESHY_TEXT_TO_3D_URL}/{task_id}")
        response.raise_for_status()
        return response.json()

    def _submit_refine(self, preview_task_id: str) -> str:
        response = self._client.post(
            MESHY_TEXT_TO_3D_URL,
            json={
                "mode": "refine",
                "preview_task_id": preview_task_id,
                "enable_pbr": self._text_enable_pbr,
                "texture_resolution": self._text_texture_resolution,
            },
        )
        response.raise_for_status()
        refine_task_id = response.json()["result"]
        logger.info("meshy: preview %s succeeded, submitted refine task %s", preview_task_id, refine_task_id)
        return refine_task_id

    def _get_text_status(self, task_id: str, state: _TextTaskState) -> MeshTaskStatus:
        with state.refine_lock:
            refine_id = state.refine_task_id
        if refine_id is not None:
            body = self._get_text_to_3d(refine_id)
            meshy_status = body.get("status", "")
            if meshy_status == "SUCCEEDED":
                return MeshTaskStatus(status="ready", progress=100)
            if meshy_status in ("FAILED", "CANCELED"):
                error = (body.get("task_error") or {}).get("message") or meshy_status
                return MeshTaskStatus(status="failed", progress=0, error=error)
            # PENDING/IN_PROGRESS (or unknown): refine is the back half.
            progress = 50 + int(body.get("progress", 0)) // 2
            return MeshTaskStatus(status="pending", progress=min(progress, 100))

        # No refine yet: this is still the preview phase.
        body = self._get_text_to_3d(task_id)
        meshy_status = body.get("status", "")
        if meshy_status in ("FAILED", "CANCELED"):
            error = (body.get("task_error") or {}).get("message") or meshy_status
            return MeshTaskStatus(status="failed", progress=0, error=error)
        if meshy_status == "SUCCEEDED":
            # Kick off refine exactly once, even if multiple threads observe
            # "preview succeeded" at the same time.
            with state.refine_lock:
                if state.refine_task_id is None:
                    state.refine_task_id = self._submit_refine(task_id)
            return MeshTaskStatus(status="pending", progress=50)
        if meshy_status not in ("PENDING", "IN_PROGRESS"):
            logger.warning("meshy: unknown text-to-3d status %r for preview task %s", meshy_status, task_id)
        # PENDING/IN_PROGRESS (or unknown): preview is the front half.
        return MeshTaskStatus(status="pending", progress=int(body.get("progress", 0)) // 2)

    def _extract_and_cache_glb(self, task_id: str, body: dict) -> bytes | None:
        """Shared tail for both image-to-3d and text-to-3d fetch_model: given
        a Meshy status body, download and cache its model_urls.glb (keyed by
        our public task_id) if the task succeeded."""
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
