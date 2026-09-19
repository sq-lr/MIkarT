from __future__ import annotations

import logging

from fastapi import APIRouter, File, Form, HTTPException, UploadFile

from app.models.world_recipe import WorldRecipeResponse
from app.services.mesh_generation_service import MeshTask
from app.services.object_cropper import crop_objects
from app.services.providers import get_providers

logger = logging.getLogger(__name__)

router = APIRouter()

_ALLOWED_CONTENT_TYPES = {"image/jpeg", "image/png"}


@router.post("/generate-world", response_model=WorldRecipeResponse, response_model_exclude_none=True)
async def generate_world(
    image: UploadFile = File(...),
    description: str = Form(..., min_length=1, max_length=500),
) -> WorldRecipeResponse:
    if image.content_type not in _ALLOWED_CONTENT_TYPES:
        raise HTTPException(status_code=400, detail="image must be image/jpeg or image/png")

    image_bytes = await image.read()
    providers = get_providers()

    try:
        # 1. VLM: scene understanding + the objects worth turning into props.
        scene = providers.vision.analyze_image(image_bytes, description)

        # 2. Cut each detected object out of the source image.
        crops = crop_objects(image_bytes, scene.detected_objects, providers.max_objects_per_world)

        # 3. Kick off mesh generation per crop. Returns immediately -- Unity
        #    polls /assets/{task_id}. A failed submit only costs that object
        #    its mesh (it keeps a placeholder); it never fails the world.
        mesh_tasks: list[MeshTask] = []
        for crop in crops:
            try:
                task = providers.mesh.submit(crop)
            except Exception:
                logger.exception("mesh submit failed for %s; object keeps its placeholder", crop.label)
                continue
            if task is not None:
                providers.registry.add(task)
                mesh_tasks.append(task)

        # 4. Assemble the recipe; objects carry their task handles.
        recipe = providers.synthesis.synthesize(scene, description, mesh_tasks)
    except Exception:
        logger.exception("world generation failed")
        raise HTTPException(status_code=500, detail="world generation failed")

    return WorldRecipeResponse(world_recipe=recipe)
