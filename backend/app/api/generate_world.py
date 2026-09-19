from __future__ import annotations

import logging

from fastapi import APIRouter, File, Form, HTTPException, UploadFile

from app.models.world_recipe import WorldRecipeResponse
from app.services.vision_service import MockVisionService
from app.services.world_synthesis_service import MockWorldSynthesisService

logger = logging.getLogger(__name__)

router = APIRouter()

_ALLOWED_CONTENT_TYPES = {"image/jpeg", "image/png"}

# Bootstrap wiring: mock implementations only. Swapping these for real
# providers later does not change the route signature or response shape.
_vision_service = MockVisionService()
_world_synthesis_service = MockWorldSynthesisService()


@router.post("/generate-world", response_model=WorldRecipeResponse)
async def generate_world(
    image: UploadFile = File(...),
    description: str = Form(..., min_length=1, max_length=500),
) -> WorldRecipeResponse:
    if image.content_type not in _ALLOWED_CONTENT_TYPES:
        raise HTTPException(status_code=400, detail="image must be image/jpeg or image/png")

    image_bytes = await image.read()

    try:
        scene = _vision_service.analyze_image(image_bytes)
        recipe = _world_synthesis_service.synthesize(scene, description)
    except Exception:
        logger.exception("world generation failed")
        raise HTTPException(status_code=500, detail="world generation failed")

    return WorldRecipeResponse(world_recipe=recipe)
