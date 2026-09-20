from __future__ import annotations

import httpx
import pytest

from app.services.filler_asset_service import FillerAsset
from app.services.library_asset_service import MockLibraryAssetService, PolyPizzaLibraryAssetService

GLB_BYTES = b"glTF\x02\x00\x00\x00fake"


class FakePolyPizza:
    """Scripted stand-in for api.poly.pizza, driven through httpx.MockTransport."""

    def __init__(self):
        self.requests: list[httpx.Request] = []
        self.results: list[dict] = [{"ID": "model-123", "Title": "Rock", "Download": "https://cdn.example/model.glb"}]

    def handler(self, request: httpx.Request) -> httpx.Response:
        self.requests.append(request)
        if request.url.host == "cdn.example" and request.url.path == "/model.glb":
            return httpx.Response(200, content=GLB_BYTES)
        if request.method == "GET" and request.url.path.startswith("/v1.1/search/"):
            return httpx.Response(200, json={"results": self.results, "total": len(self.results)})
        return httpx.Response(404, json={"message": "not found"})


@pytest.fixture
def poly_pizza():
    fake = FakePolyPizza()
    service = PolyPizzaLibraryAssetService(api_key="secret", transport=httpx.MockTransport(fake.handler))
    return fake, service


def test_requires_api_key():
    with pytest.raises(ValueError):
        PolyPizzaLibraryAssetService(api_key="")


def test_submit_sends_auth_header_and_returns_task(poly_pizza):
    fake, service = poly_pizza
    asset = FillerAsset(keyword="rock", density=0.4, placement="scattered")

    task = service.submit(asset)

    assert task is not None
    assert (task.task_id, task.object_type, task.provider) == ("model-123", "rock", "polypizza")
    request = fake.requests[-1]
    assert request.headers["x-auth-token"] == "secret"
    assert request.url.path == "/v1.1/search/rock"


def test_submit_returns_none_when_no_results(poly_pizza):
    fake, service = poly_pizza
    fake.results = []
    asset = FillerAsset(keyword="nonexistent_thing", density=0.4, placement="scattered")

    assert service.submit(asset) is None


def test_submit_skips_unrelated_top_result_for_a_better_match(poly_pizza):
    # Real bug seen live: Poly Pizza's #1 result for "trash_can" was "Debris
    # Papers" (a scrap of paper, nothing to do with a trash can), with the
    # actual match ("Trash Can") sitting a few slots down the same page.
    fake, service = poly_pizza
    fake.results = [
        {"ID": "wrong-1", "Title": "Debris Papers", "Download": "https://cdn.example/wrong.glb"},
        {"ID": "wrong-2", "Title": "Dumpster", "Download": "https://cdn.example/wrong2.glb"},
        {"ID": "right-1", "Title": "Trash Can", "Download": "https://cdn.example/model.glb"},
    ]
    asset = FillerAsset(keyword="trash_can", density=0.4, placement="scattered")

    task = service.submit(asset)

    assert task is not None
    assert task.task_id == "right-1"


def test_submit_matches_compound_title_by_substring(poly_pizza):
    # Real bug seen live: no result for "street_lamp" had that exact phrase
    # as a title, but "Streetlight" (one word) is a real match once "street"
    # is checked as a substring rather than requiring an exact word match.
    fake, service = poly_pizza
    fake.results = [
        {"ID": "wrong-1", "Title": "Road Bits", "Download": "https://cdn.example/wrong.glb"},
        {"ID": "wrong-2", "Title": "Ceiling Light", "Download": "https://cdn.example/wrong2.glb"},
        {"ID": "right-1", "Title": "Streetlight", "Download": "https://cdn.example/model.glb"},
    ]
    asset = FillerAsset(keyword="street_lamp", density=0.4, placement="roadside")

    task = service.submit(asset)

    assert task is not None
    assert task.task_id == "right-1"


def test_submit_returns_none_when_nothing_on_the_page_relates_to_keyword(poly_pizza):
    fake, service = poly_pizza
    fake.results = [
        {"ID": "wrong-1", "Title": "Road Bits", "Download": "https://cdn.example/wrong.glb"},
        {"ID": "wrong-2", "Title": "Ceiling Light", "Download": "https://cdn.example/wrong2.glb"},
    ]
    asset = FillerAsset(keyword="street_lamp", density=0.4, placement="roadside")

    assert service.submit(asset) is None


def test_get_status_is_ready_immediately_after_submit(poly_pizza):
    fake, service = poly_pizza
    task = service.submit(FillerAsset(keyword="rock", density=0.4, placement="scattered"))

    status = service.get_status(task.task_id)

    assert status.status == "ready"
    assert status.progress == 100


def test_get_status_unknown_task_fails(poly_pizza):
    _, service = poly_pizza
    status = service.get_status("nope")
    assert status.status == "failed"


def test_fetch_model_downloads_and_caches(poly_pizza):
    fake, service = poly_pizza
    task = service.submit(FillerAsset(keyword="rock", density=0.4, placement="scattered"))

    assert service.fetch_model(task.task_id) == GLB_BYTES
    assert service.fetch_model(task.task_id) == GLB_BYTES
    downloads = [r for r in fake.requests if r.url.host == "cdn.example"]
    assert len(downloads) == 1


def test_fetch_model_unknown_task_returns_none(poly_pizza):
    _, service = poly_pizza
    assert service.fetch_model("nope") is None


def test_mock_library_finds_nothing_no_network():
    service = MockLibraryAssetService()
    assert service.submit(FillerAsset(keyword="rock", density=0.5, placement="scattered")) is None
    assert service.get_status("anything").status == "failed"
    assert service.fetch_model("anything") is None
