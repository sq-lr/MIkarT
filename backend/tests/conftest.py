from __future__ import annotations

import io
import os

# Tests must never touch a real vendor, whatever the developer's backend/.env
# says. app.main calls load_dotenv(), which does NOT override variables that
# are already set -- so pin every provider to mock here, before any test
# module imports the app. (Without this, a .env with MESH_PROVIDER=meshy made
# the endpoint test submit real Meshy tasks -- and a real credential sitting
# in the ambient shell/profile, e.g. an `ant auth login` session, means even
# popping ANTHROPIC_API_KEY alone isn't enough insurance for the *_PROVIDER
# vars: ANTHROPIC_AUTH_TOKEN or similar can silently satisfy the same check.)
for _provider_var in (
    "AI_PROVIDER",
    "MESH_PROVIDER",
    "TEXT_ASSET_PROVIDER",
    "FILLER_ASSET_PROVIDER",
    "ASSET_MERGE_PROVIDER",
    "LIBRARY_PROVIDER",
):
    os.environ[_provider_var] = "mock"
for _credential_var in (
    "MESHY_API_KEY",
    "POLYPIZZA_API_KEY",
    "ANTHROPIC_API_KEY",
    "ANTHROPIC_AUTH_TOKEN",
):
    os.environ.pop(_credential_var, None)

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
