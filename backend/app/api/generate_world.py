from __future__ import annotations

import logging
from concurrent.futures import ThreadPoolExecutor

from fastapi import APIRouter, File, Form, HTTPException, UploadFile

from app.models.world_recipe import WorldRecipeResponse
from app.services.mesh_generation_service import MeshGenerationService, MeshTask
from app.services.object_cropper import ObjectCrop, crop_objects
from app.services.providers import get_providers
from app.services.text_asset_service import ExtractedAsset

logger = logging.getLogger(__name__)

router = APIRouter()

_ALLOWED_CONTENT_TYPES = {"image/jpeg", "image/png"}


@router.post("/generate-world", response_model=WorldRecipeResponse, response_model_exclude_none=True)
async def generate_world(
    image: UploadFile = File(...),
    description: str = Form(..., min_length=1, max_length=500),
    sky: str = Form("sunny"),
) -> WorldRecipeResponse:
    if image.content_type not in _ALLOWED_CONTENT_TYPES:
        raise HTTPException(status_code=400, detail="image must be image/jpeg or image/png")

    image_bytes = await image.read()
    providers = get_providers()

    try:
        # 1. VLM + text extraction are independent Claude calls with no data
        #    dependency on each other, so run them concurrently -- otherwise
        #    they'd stack to roughly double the latency for no reason.
        with ThreadPoolExecutor(max_workers=2) as claude_pool:
            vision_future = claude_pool.submit(providers.vision.analyze_image, image_bytes, description)
            text_future = claude_pool.submit(providers.text_assets.extract, description)

            scene = vision_future.result()  # vision failure fails the world, same as before

            try:
                key_assets = text_future.result().key_assets[: providers.max_text_assets_per_world]
            except Exception:
                # Text-derived assets are a bonus on top of an already-complete
                # photo-derived world: a broken extraction call only costs
                # those extra objects, it never fails the whole request.
                logger.exception("text asset extraction failed; continuing with photo objects only")
                key_assets = []

        # 2. Cut each detected object out of the source image.
        crops = crop_objects(image_bytes, scene.detected_objects, providers.max_objects_per_world)

        # 3. Kick off mesh generation, one submission at a time -- image crops
        #    then text assets. Unity polls /assets/{task_id} afterwards. A
        #    failed submit only costs that object its mesh (it keeps a
        #    placeholder); it never fails the world.
        image_mesh_tasks, text_mesh_tasks = _submit_mesh_tasks(providers.mesh, crops, key_assets)
        for task in image_mesh_tasks + text_mesh_tasks:
            providers.registry.add(task)

        # 4. Assemble the recipe; objects carry their task handles.
        recipe = providers.synthesis.synthesize(
            scene,
            description,
            image_mesh_tasks,
            key_assets,
            text_mesh_tasks,
            sky=sky,
        )
    except Exception:
        logger.exception("world generation failed")
        raise HTTPException(status_code=500, detail="world generation failed")

    return WorldRecipeResponse(world_recipe=recipe)


def _submit_mesh_tasks(
    mesh: MeshGenerationService,
    crops: list[ObjectCrop],
    text_assets: list[ExtractedAsset],
) -> tuple[list[MeshTask], list[MeshTask]]:
    """Submit every crop, then every text asset, one at a time.

    Sequential by design: each submission is its own HTTP round-trip, and a
    shared image_mesh_tasks/text_mesh_tasks split lets synthesis attach the
    right kind of task to the right kind of object entry.
    """

    def submit_crop(crop: ObjectCrop) -> MeshTask | None:
        try:
            return mesh.submit(crop)
        except Exception:
            logger.exception("mesh submit failed for %s; object keeps its placeholder", crop.label)
            return None

    def submit_text(asset: ExtractedAsset) -> MeshTask | None:
        try:
            return mesh.submit_text(asset)
        except Exception:
            logger.exception("text-to-3d submit failed for %s; object is dropped", asset.label)
            return None

    image_tasks = [task for crop in crops if (task := submit_crop(crop)) is not None]
    text_tasks = [task for asset in text_assets if (task := submit_text(asset)) is not None]

    return image_tasks, text_tasks
