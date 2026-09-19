from __future__ import annotations

import io
import os

# Tests must never touch a real vendor, whatever the developer's backend/.env
# says. app.main calls load_dotenv(), which does NOT override variables that
# are already set -- so pin the mock providers here, before any test module
# imports the app. (Without this, a .env with MESH_PROVIDER=meshy made the
# endpoint test submit real Meshy tasks.)
os.environ["AI_PROVIDER"] = "mock"
os.environ["MESH_PROVIDER"] = "mock"
os.environ.pop("MESHY_API_KEY", None)
os.environ.pop("ANTHROPIC_API_KEY", None)

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
