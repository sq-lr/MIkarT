from __future__ import annotations

import logging
from concurrent.futures import ThreadPoolExecutor

from fastapi import APIRouter, File, Form, HTTPException, UploadFile

from app.models.world_recipe import WorldRecipeResponse
from app.services.asset_merge_service import AssetMergeService, MergeCandidate
from app.services.filler_asset_service import FillerAsset
from app.services.library_asset_service import LibraryAssetService
from app.services.mesh_generation_service import MeshGenerationService, MeshTask
from app.services.object_cropper import ObjectCrop, crop_objects
from app.services.providers import get_providers
from app.services.text_asset_service import ExtractedAsset
from app.services.vision_service import DetectedObject
from app.services.world_synthesis_service import MAX_LANDMARKS

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
    # Off by default (see the upload screen's "Generate personalized assets"
    # toggle): skips Meshy entirely and sources every object -- including up
    # to MAX_LANDMARKS landmarks -- from library retrieval instead. See
    # docs/decisions/0012-personalize-toggle.md.
    personalize: bool = Form(False),
) -> WorldRecipeResponse:
    if image.content_type not in _ALLOWED_CONTENT_TYPES:
        raise HTTPException(status_code=400, detail="image must be image/jpeg or image/png")

    description = description.strip() or DEFAULT_DESCRIPTION

    image_bytes = await image.read()
    providers = get_providers()

    # Filler is the only source allowed a landmark when nothing personalized
    # is being generated -- otherwise the world would have no centrepiece.
    filler_max_landmarks = 0 if personalize else MAX_LANDMARKS

    try:
        # 1. VLM, text extraction, and filler suggestion are independent
        #    Claude calls with no data dependency on each other, so run
        #    whichever ones are needed concurrently -- otherwise they'd stack
        #    to roughly N times the latency for no reason. Text extraction is
        #    skipped entirely when not personalizing: its whole purpose is
        #    vivid Meshy generation prompts, which won't be submitted anyway.
        with ThreadPoolExecutor(max_workers=3 if personalize else 2) as claude_pool:
            vision_future = claude_pool.submit(providers.vision.analyze_image, image_bytes, description)
            filler_future = claude_pool.submit(
                providers.filler_assets.suggest, image_bytes, description, filler_max_landmarks
            )
            text_future = claude_pool.submit(providers.text_assets.extract, description) if personalize else None

            scene = vision_future.result()  # vision failure fails the world, same as before

            if text_future is not None:
                try:
                    key_assets = text_future.result().key_assets[: providers.max_text_assets_per_world]
                except Exception:
                    # Text-derived assets are a bonus on top of an already-complete
                    # photo-derived world: a broken extraction call only costs
                    # those extra objects, it never fails the whole request.
                    logger.exception("text asset extraction failed; continuing with photo objects only")
                    key_assets = []
            else:
                key_assets = []

            try:
                filler_extraction = filler_future.result()
                filler_candidates = filler_extraction.filler_assets[: providers.max_filler_assets_per_world]
                ground_color = filler_extraction.ground_color
            except Exception:
                # Same reasoning as text extraction: filler is pure bonus.
                logger.exception("filler asset suggestion failed; continuing without extra filler")
                filler_candidates = []
                ground_color = None

        if sky == "indoor":
            filler_candidates = _indoor_background(filler_candidates)

        # 2. personalize=True pulls from three independent sources (photo,
        #    text, filler) that never saw each other's suggestions, so the
        #    combined pool can have competing "background"/"landmark"
        #    candidates with no reconciliation. Ask one more (small, fast)
        #    Claude call to decide the actual final composition before
        #    anything is submitted to Meshy/the library, so a candidate it
        #    drops never costs a wasted call. personalize=False skips this
        #    entirely: filler alone already stages its own single
        #    background/landmark/fill-the-rest composition in one call, so
        #    there's nothing to merge across sources.
        if personalize:
            scene.detected_objects, key_assets, filler_candidates = _merge_assets(
                providers.asset_merge,
                scene.detected_objects,
                key_assets,
                filler_candidates,
                providers.max_assets_per_world,
            )

        # 3. Cut each detected object out of the source image -- skipped
        #    entirely when not personalizing, since nothing will be submitted
        #    to Meshy for them anyway.
        crops = crop_objects(image_bytes, scene.detected_objects, providers.max_objects_per_world) if personalize else []

        # 4. Kick off mesh generation, one submission at a time -- image crops
        #    then text assets (both empty when not personalizing), then a
        #    library search per filler keyword. Unity polls /assets/{task_id}
        #    afterwards. A failed submit only costs that object its mesh (it
        #    keeps a placeholder, or for filler is simply dropped); it never
        #    fails the world.
        image_mesh_tasks, text_mesh_tasks = _submit_mesh_tasks(providers.mesh, crops, key_assets)
        filler_candidates, filler_mesh_tasks = _submit_filler_assets(providers.library, filler_candidates)
        for task in image_mesh_tasks + text_mesh_tasks + filler_mesh_tasks:
            providers.registry.add(task)

        # 5. Assemble the recipe; objects carry their task handles.
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
            personalize=personalize,
            ground_color=ground_color,
        )
    except Exception:
        logger.exception("world generation failed")
        raise HTTPException(status_code=500, detail="world generation failed")

    return WorldRecipeResponse(world_recipe=recipe)


def _merge_assets(
    merge_service: AssetMergeService,
    photo_objects: list[DetectedObject],
    text_objects: list[ExtractedAsset],
    filler_objects: list[FillerAsset],
    max_assets: int,
) -> tuple[list[DetectedObject], list[ExtractedAsset], list[FillerAsset]]:
    """Ask the merge service for the final landmark/roadside/scattered
    composition across all three extraction sources, then filter and
    re-place each source's own list to match. Candidates are addressed by a
    shared index (photo, then text, then filler) so the merge service only
    has to hand back keep/placement decisions -- every other field (bbox,
    prompt, keyword, ...) is left untouched, since only the source that
    produced an object knows how to submit it for a mesh.

    "background" is hardcoded to always be filler's candidate, never left to
    the merge call to arbitrate: filler_asset_extraction always suggests
    exactly one (see its prompt's BACKGROUND step), so a photo/text object
    that also happens to say "background" (a skyline visible in the photo,
    say) is demoted to "scattered" before the merge ever runs -- it was
    never eligible for the slot -- and filler's own background candidate(s)
    are excluded from the candidate pool entirely and always kept, unchanged,
    in the result. This removes an entire failure mode (the merge picking a
    less-fitting photo/text "background" over filler's, or -- worse --
    picking none) at zero cost, since filler's suggestion is guaranteed to
    exist and was already designed to answer exactly this question.

    On any failure (including an empty result covering nothing), each
    source's own list is returned unchanged -- world_synthesis_service's
    existing per-source demotion/truncation logic is the backstop either
    way, so a broken merge call only costs its own curation, never the
    world."""
    for obj in photo_objects:
        if obj.placement == "background":
            obj.placement = "scattered"
    for asset in text_objects:
        if asset.placement == "background":
            asset.placement = "scattered"

    filler_background = [a for a in filler_objects if a.placement == "background"]
    filler_mergeable = [a for a in filler_objects if a.placement != "background"]

    candidates: list[MergeCandidate] = []
    for i, obj in enumerate(photo_objects):
        candidates.append(MergeCandidate(index=i, source="photo", label=obj.label, placement=obj.placement, density=obj.prominence))
    text_start = len(photo_objects)
    for i, asset in enumerate(text_objects):
        candidates.append(
            MergeCandidate(index=text_start + i, source="text", label=asset.label, placement=asset.placement, density=asset.density)
        )
    filler_start = text_start + len(text_objects)
    for i, asset in enumerate(filler_mergeable):
        candidates.append(
            MergeCandidate(
                index=filler_start + i, source="filler", label=asset.keyword, placement=asset.placement, density=asset.density
            )
        )

    if not candidates:
        return photo_objects, text_objects, filler_objects

    try:
        result = merge_service.merge(candidates, max_assets)
    except Exception:
        logger.exception("asset merge failed; keeping each source's own placement unchanged")
        return photo_objects, text_objects, filler_objects

    by_index = {d.index: d for d in result.decisions}

    def apply(objs: list, start: int) -> list:
        kept = []
        for i, obj in enumerate(objs):
            decision = by_index.get(start + i)
            if decision is None or not decision.keep:
                continue
            obj.placement = decision.placement
            kept.append(obj)
        return kept

    new_photo = apply(photo_objects, 0)
    new_text = apply(text_objects, text_start)
    new_filler = filler_background + apply(filler_mergeable, filler_start)
    return new_photo, new_text, new_filler


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


INDOOR_BACKGROUND_KEYWORD = "wall"


def _indoor_background(filler_assets: list[FillerAsset]) -> list[FillerAsset]:
    """For the "indoor" sky preset, the continuous background wall along the
    track (EnvironmentGenerator.PlaceBackgroundWall) is hardcoded to library
    "wall" pieces: whatever the filler extractor suggested for the horizon
    (mountains, a treeline) would look wrong inside a room. Every background
    keyword collapses into one "wall" entry at the densest of their densities
    (0.6 if there were none), keeping every other placement as suggested.
    """
    backgrounds = [a for a in filler_assets if a.placement == "background"]
    density = max((a.density for a in backgrounds), default=0.6)
    kept = [a for a in filler_assets if a.placement != "background"]
    return kept + [FillerAsset(keyword=INDOOR_BACKGROUND_KEYWORD, density=density, placement="background")]


def _submit_filler_assets(
    library: LibraryAssetService, filler_assets: list[FillerAsset]
) -> tuple[list[FillerAsset], list[MeshTask]]:
    """Search the library for each filler keyword, one at a time. A keyword
    with no match (or a failed search) simply isn't added -- filler was never
    essential, so there's nothing to fall back to for it.

    A "background" keyword is the continuous mountain/treeline wall lining
    the whole track (EnvironmentGenerator.PlaceBackgroundWall) -- with only
    one matched mesh, that wall would visibly repeat the same shape hundreds
    of times. So background keywords additionally get a second, genuinely
    different match submitted (excluding the first result's ID) under a
    synthetic f"{keyword}_2" label, giving Unity's wall two shapes to
    alternate between. Returns the (possibly expanded with variants) asset
    list alongside its tasks, since the caller passes both into synthesize().
    """

    def submit_one(asset: FillerAsset, exclude_ids: frozenset[str] = frozenset()) -> MeshTask | None:
        try:
            return library.submit(asset, exclude_ids=exclude_ids)
        except Exception:
            logger.exception("library search failed for %r; filler dropped", asset.keyword)
            return None

    assets: list[FillerAsset] = []
    tasks: list[MeshTask] = []
    for asset in filler_assets:
        task = submit_one(asset)
        if task is None:
            continue
        assets.append(asset)
        tasks.append(task)

        if asset.placement == "background":
            variant_task = submit_one(asset, exclude_ids=frozenset({task.task_id}))
            if variant_task is not None:
                variant_keyword = f"{asset.keyword}_2"
                assets.append(asset.model_copy(update={"keyword": variant_keyword}))
                tasks.append(MeshTask(task_id=variant_task.task_id, object_type=variant_keyword, provider=variant_task.provider))

    return assets, tasks
