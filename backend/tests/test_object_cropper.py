from __future__ import annotations

import io

from PIL import Image

from app.services.object_cropper import MIN_CROP_PIXELS, crop_objects
from app.services.vision_service import DetectedObject
from tests.conftest import make_png


def _size(png_bytes: bytes) -> tuple[int, int]:
    return Image.open(io.BytesIO(png_bytes)).size


def test_crop_is_padded_and_encoded_as_png():
    image = make_png(200, 100)
    crops = crop_objects(image, [DetectedObject(label="rock", bbox=[0.25, 0.25, 0.5, 0.5], prominence=0.5)], max_objects=4)

    assert len(crops) == 1
    crop = crops[0]
    assert crop.label == "rock"
    assert crop.png_bytes.startswith(b"\x89PNG")
    # bbox 50..150 x 25..75, padded by 5% of the box (5px / 2.5px) each side.
    assert crop.bbox == (45, 22, 155, 78)
    assert _size(crop.png_bytes) == (110, 56)


def test_bbox_is_clamped_to_image_bounds():
    image = make_png(100, 100)
    crops = crop_objects(image, [DetectedObject(label="tree", bbox=[0.6, 0.6, 0.4, 0.4], prominence=0.5)], max_objects=4)

    assert crops[0].bbox[2] == 100 and crops[0].bbox[3] == 100


def test_degenerate_crops_are_dropped_and_max_objects_respected():
    image = make_png(400, 400)
    detections = [
        DetectedObject(label="big", bbox=[0.0, 0.0, 0.5, 0.5], prominence=0.9),
        DetectedObject(label="tiny", bbox=[0.5, 0.5, 0.01, 0.01], prominence=0.1),  # 4px -> dropped
        DetectedObject(label="second", bbox=[0.5, 0.0, 0.5, 0.5], prominence=0.8),
        DetectedObject(label="third", bbox=[0.0, 0.5, 0.5, 0.5], prominence=0.7),
    ]
    crops = crop_objects(image, detections, max_objects=3)

    labels = [c.label for c in crops]
    assert labels == ["big", "second"]
    assert all(min(_size(c.png_bytes)) >= MIN_CROP_PIXELS for c in crops)


def test_no_detections_or_bad_image_yield_no_crops():
    assert crop_objects(make_png(), [], max_objects=4) == []
    assert crop_objects(b"not an image", [DetectedObject(label="x", bbox=[0, 0, 1, 1], prominence=1)], max_objects=4) == []
