"""Cut the VLM's detected objects out of the source image.

Pure function, no I/O: takes the uploaded image bytes plus the normalized
bounding boxes from SceneUnderstanding.detected_objects and returns one PNG
crop per object, ready to be handed to the mesh provider.
"""

from __future__ import annotations

import io
import logging
from dataclasses import dataclass

from PIL import Image, UnidentifiedImageError

from app.services.vision_service import DetectedObject

logger = logging.getLogger(__name__)

# Crops smaller than this on either side are useless as image-to-3D input.
MIN_CROP_PIXELS = 32


@dataclass(frozen=True)
class ObjectCrop:
    label: str
    png_bytes: bytes
    bbox: tuple[int, int, int, int]  # pixel-space (left, top, right, bottom)


def crop_objects(
    image_bytes: bytes,
    detected_objects: list[DetectedObject],
    max_objects: int,
    padding: float = 0.05,
) -> list[ObjectCrop]:
    """Return up to `max_objects` crops, in the VLM's importance order.

    `padding` is a fraction of the bbox's own size added on each side so the
    crop isn't cut flush against the object's silhouette. Boxes are clamped to
    the image, and crops that end up degenerate (< MIN_CROP_PIXELS) are
    dropped rather than raising -- a bad bbox shouldn't fail the whole world.
    """
    if not detected_objects:
        return []

    try:
        with Image.open(io.BytesIO(image_bytes)) as source:
            image = source.convert("RGBA")
    except (UnidentifiedImageError, OSError):
        # An undecodable upload shouldn't fail the world: the objects simply
        # get no crops, hence no meshes, and Unity keeps its placeholders.
        logger.warning("could not decode uploaded image; skipping object crops")
        return []

    width, height = image.size
    crops: list[ObjectCrop] = []

    for obj in detected_objects[:max_objects]:
        x, y, w, h = obj.bbox
        pad_x = w * padding
        pad_y = h * padding

        left = int(round(max(0.0, x - pad_x) * width))
        top = int(round(max(0.0, y - pad_y) * height))
        right = int(round(min(1.0, x + w + pad_x) * width))
        bottom = int(round(min(1.0, y + h + pad_y) * height))

        if right - left < MIN_CROP_PIXELS or bottom - top < MIN_CROP_PIXELS:
            continue

        buffer = io.BytesIO()
        image.crop((left, top, right, bottom)).save(buffer, format="PNG")
        crops.append(ObjectCrop(label=obj.label, png_bytes=buffer.getvalue(), bbox=(left, top, right, bottom)))

    return crops
