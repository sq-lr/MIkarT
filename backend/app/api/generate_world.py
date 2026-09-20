from __future__ import annotations

import logging
from concurrent.futures import ThreadPoolExecutor

from fastapi import APIRouter, File, Form, HTTPException, UploadFile

from app.models.world_recipe import WorldRecipeResponse
from app.services.filler_asset_service import FillerAsset
from app.services.library_asset_service import LibraryAssetService
from app.services.mesh_generation_service import MeshGenerationService, MeshTask
from app.services.object_cropper import ObjectCrop, crop_objects
from app.services.providers import get_providers
from app.services.text_asset_service import ExtractedAsset

logger = logging.getLogger(__name__)

router = APIRouter()

_ALLOWED_CONTENT_TYPES = {"image/jpeg", "image/png"}

# Used when the player leaves the description blank. Rejecting the request
# instead (422) made Unity silently fall back to the offline world, which
# looked like "generation didn't work" -- the photo alone is enough input.
DEFAULT_DESCRIPTION = "a racing world inspired by this photo"


@router.post("/generate-world", response_model=WorldRecipeResponse, response_model_exclude_none=True)
async def generate_world(
    image: UploadFile = File(...),
    description: str = Form("", max_length=500),
    sky: str = Form("sunny"),
) -> WorldRecipeResponse:
    if image.content_type not in _ALLOWED_CONTENT_TYPES:
        raise HTTPException(status_code=400, detail="image must be image/jpeg or image/png")

    description = description.strip() or DEFAULT_DESCRIPTION

    image_bytes = await image.read()
    providers = get_providers()

    try:
        # 1. VLM, text extraction, and filler suggestion are three independent
        #    Claude calls with no data dependency on each other, so run them
        #    concurrently -- otherwise they'd stack to roughly triple the
        #    latency for no reason.
        with ThreadPoolExecutor(max_workers=3) as claude_pool:
            vision_future = claude_pool.submit(providers.vision.analyze_image, image_bytes, description)
            text_future = claude_pool.submit(providers.text_assets.extract, description)
            filler_future = claude_pool.submit(providers.filler_assets.suggest, image_bytes, description)

            scene = vision_future.result()  # vision failure fails the world, same as before

            try:
                key_assets = text_future.result().key_assets[: providers.max_text_assets_per_world]
            except Exception:
                # Text-derived assets are a bonus on top of an already-complete
                # photo-derived world: a broken extraction call only costs
                # those extra objects, it never fails the whole request.
                logger.exception("text asset extraction failed; continuing with photo objects only")
                key_assets = []

            try:
                filler_candidates = filler_future.result().filler_assets[: providers.max_filler_assets_per_world]
            except Exception:
                # Same reasoning as text extraction: filler is pure bonus.
                logger.exception("filler asset suggestion failed; continuing without extra filler")
                filler_candidates = []

        # 2. Cut each detected object out of the source image.
        crops = crop_objects(image_bytes, scene.detected_objects, providers.max_objects_per_world)

        # 3. Kick off mesh generation, one submission at a time -- image crops
        #    then text assets, then a library search per filler keyword.
        #    Unity polls /assets/{task_id} afterwards. A failed submit only
        #    costs that object its mesh (it keeps a placeholder, or for
        #    filler is simply dropped); it never fails the world.
        image_mesh_tasks, text_mesh_tasks = _submit_mesh_tasks(providers.mesh, crops, key_assets)
        filler_mesh_tasks = _submit_filler_assets(providers.library, filler_candidates)
        for task in image_mesh_tasks + text_mesh_tasks + filler_mesh_tasks:
            providers.registry.add(task)

        # 4. Assemble the recipe; objects carry their task handles.
        recipe = providers.synthesis.synthesize(
            scene,
            description,
            image_mesh_tasks,
            key_assets,
            text_mesh_tasks,
            filler_candidates,
            filler_mesh_tasks,
            sky=sky,
            max_assets=providers.max_assets_per_world,
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


def _submit_filler_assets(library: LibraryAssetService, filler_assets: list[FillerAsset]) -> list[MeshTask]:
    """Search the library for each filler keyword, one at a time. A keyword
    with no match (or a failed search) simply isn't added -- filler was never
    essential, so there's nothing to fall back to for it."""

    def submit_one(asset: FillerAsset) -> MeshTask | None:
        try:
            return library.submit(asset)
        except Exception:
            logger.exception("library search failed for %r; filler dropped", asset.keyword)
            return None

    return [task for asset in filler_assets if (task := submit_one(asset)) is not None]
