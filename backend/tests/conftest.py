from __future__ import annotations

import io

import pytest
from PIL import Image

from app.services import providers as providers_module


@pytest.fixture(autouse=True)
def reset_providers():
    """Every test starts from the env-default (mock) providers."""
    providers_module.override(None)
    yield
    providers_module.override(None)


def make_png(width: int = 200, height: int = 120, color=(200, 50, 50, 255)) -> bytes:
    buffer = io.BytesIO()
    Image.new("RGBA", (width, height), color).save(buffer, format="PNG")
    return buffer.getvalue()
