"""Unity's second (and last) backend surface: poll for, then download, the
meshes that /generate-world kicked off. See
docs/decisions/0006-meshy-async-mesh-generation.md for why this is async and
why the GLB is proxied through here instead of fetched from the provider."""

from __future__ import annotations

import logging

from fastapi import APIRouter, HTTPException, Response

from app.services.mesh_generation_service import MeshGenerationService, MeshTaskStatus
from app.services.library_asset_service import LibraryAssetService
from app.services.providers import Providers, get_providers

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/assets")

GLB_MEDIA_TYPE = "model/gltf-binary"


def _service_for(providers: Providers, task_id: str) -> MeshGenerationService | LibraryAssetService | None:
    """Both Meshy tasks and Poly Pizza tasks share one task_id namespace (the
    registry), but are backed by different services -- dispatch by whichever
    provider actually registered this task."""
    task = providers.registry.get(task_id)
    if task is None:
        return None
    return providers.library if task.provider == "polypizza" else providers.mesh


@router.get("/{task_id}", response_model=MeshTaskStatus, response_model_exclude_none=True)
def get_asset_status(task_id: str) -> MeshTaskStatus:
    providers = get_providers()
    service = _service_for(providers, task_id)
    if service is None:
        raise HTTPException(status_code=404, detail="unknown asset task")

    try:
        return service.get_status(task_id)
    except Exception:
        # A transient provider error shouldn't be reported as a permanent
        # failure -- Unity will simply poll again.
        logger.exception("asset status lookup failed for %s", task_id)
        return MeshTaskStatus(status="pending", error="provider status lookup failed")


@router.get("/{task_id}/model.glb")
def get_asset_model(task_id: str) -> Response:
    providers = get_providers()
    service = _service_for(providers, task_id)
    if service is None:
        raise HTTPException(status_code=404, detail="unknown asset task")

    try:
        glb = service.fetch_model(task_id)
    except Exception:
        logger.exception("asset download failed for %s", task_id)
        raise HTTPException(status_code=502, detail="asset download failed")

    if glb is None:
        raise HTTPException(status_code=409, detail="asset not ready")

    return Response(content=glb, media_type=GLB_MEDIA_TYPE)
