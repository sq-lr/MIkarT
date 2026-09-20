"""Generic filler keyword -> a real 3D mesh retrieved from a public asset
library, instead of generated.

Unlike Meshy (mesh_generation_service.py), retrieval from a library like Poly
Pizza is synchronous -- there's no generation job to poll. To let Unity's
GeneratedMeshLoader treat this identically to a Meshy task (poll
GET /assets/{task_id} until ready, then download), `submit` resolves the
search immediately and registers a task that is already effectively "ready"
by the time it's returned; `get_status` and `fetch_model` never actually wait
on anything.

Two implementations ship:
- MockLibraryAssetService (default): finds nothing, so filler keywords are
  simply dropped (they were never essential -- the world is already complete
  without them). Needs no key or network.
- PolyPizzaLibraryAssetService (LIBRARY_PROVIDER=polypizza): Poly Pizza's
  keyword search (https://poly.pizza/docs/api/v1.1).
"""

from __future__ import annotations

import logging
import threading
from abc import ABC, abstractmethod

import httpx
from pydantic import BaseModel

from app.services.filler_asset_service import FillerAsset
from app.services.mesh_generation_service import MeshTask, MeshTaskStatus

logger = logging.getLogger(__name__)


class LibraryAssetService(ABC):
    @abstractmethod
    def submit(self, asset: FillerAsset) -> MeshTask | None:
        """Search the library for asset.keyword. Returns None if nothing
        matched (the object is simply dropped -- unlike Meshy, there's no
        generation fallback) or this provider has no library (mock); raises
        on a real provider error."""
        raise NotImplementedError

    @abstractmethod
    def get_status(self, task_id: str) -> MeshTaskStatus:
        raise NotImplementedError

    @abstractmethod
    def fetch_model(self, task_id: str) -> bytes | None:
        raise NotImplementedError


class MockLibraryAssetService(LibraryAssetService):
    """Bootstrap/offline default: no library to search, every filler keyword
    is simply dropped."""

    def submit(self, asset: FillerAsset) -> MeshTask | None:
        return None

    def get_status(self, task_id: str) -> MeshTaskStatus:
        return MeshTaskStatus(status="failed", error="mock provider has no library")

    def fetch_model(self, task_id: str) -> bytes | None:
        return None


POLY_PIZZA_BASE_URL = "https://api.poly.pizza/v1.1"


class _SearchResult(BaseModel):
    ID: str
    Title: str
    Download: str


def _best_match(keyword: str, results: list[dict]) -> dict | None:
    """Pick the first result whose Title actually relates to the keyword.

    Poly Pizza's ranking is loose full-text search, not exact keyword
    matching -- its own #1 result for "trash_can" was "Debris Papers" (a
    scrap of paper) and for "street_lamp" was "Road Bits" (unrelated), while
    real matches ("Trash Can", "Streetlight") sat lower in the same page.
    Blindly taking results[0] silently attaches the wrong mesh. Instead,
    scan the page (32 results by default -- enough in practice; a real match
    for "street_lamp" showed up at position 13) for the first title
    containing any of the keyword's words as a substring (so "street" also
    matches the compound title "Streetlight"). No match anywhere in the page
    means no confident match at all -- treated the same as zero results.
    """
    keyword_words = [w for w in keyword.lower().replace("_", " ").split() if w]
    for result in results:
        title_lower = result["Title"].lower()
        if any(word in title_lower for word in keyword_words):
            return result
    return None


class PolyPizzaLibraryAssetService(LibraryAssetService):
    """Poly Pizza (https://poly.pizza/docs/api/v1.1): a free library of
    CC0/CC-BY low-poly models delivered as glTF -- the same format Meshy
    returns, so Unity's glTFast import path is unchanged.

    submit  -> GET /search/{keyword}, pick the best-matching result (see
               _best_match), resolve (but don't yet download) its GLB url
    status  -> always "ready" once submitted: the search already happened,
               there's no generation job to poll
    fetch   -> download the resolved GLB url, cached in-process so Unity's
               download and any retry don't re-hit the CDN
    """

    def __init__(
        self,
        api_key: str,
        base_url: str = POLY_PIZZA_BASE_URL,
        transport: httpx.BaseTransport | None = None,
        timeout: float = 15.0,
    ):
        if not api_key:
            raise ValueError("POLYPIZZA_API_KEY is required for LIBRARY_PROVIDER=polypizza")
        self._client = httpx.Client(
            base_url=base_url,
            headers={"X-Auth-Token": api_key},
            timeout=timeout,
            transport=transport,
        )
        self._download_urls: dict[str, str] = {}
        self._model_cache: dict[str, bytes] = {}
        self._lock = threading.Lock()

    def submit(self, asset: FillerAsset) -> MeshTask | None:
        response = self._client.get(f"/search/{asset.keyword}")
        response.raise_for_status()
        body = response.json()
        results = body.get("results") or []
        best = _best_match(asset.keyword, results)
        if best is None:
            logger.info("poly pizza: no confident match for keyword %r (%d raw results)", asset.keyword, len(results))
            return None

        result = _SearchResult.model_validate(best)
        with self._lock:
            self._download_urls[result.ID] = result.Download
        logger.info("poly pizza: matched %r -> model %s (%r)", asset.keyword, result.ID, result.Title)
        return MeshTask(task_id=result.ID, object_type=asset.keyword, provider="polypizza")

    def get_status(self, task_id: str) -> MeshTaskStatus:
        with self._lock:
            known = task_id in self._download_urls or task_id in self._model_cache
        if not known:
            return MeshTaskStatus(status="failed", error="unknown task")
        # Retrieval already happened at submit time -- nothing left to wait on.
        return MeshTaskStatus(status="ready", progress=100)

    def fetch_model(self, task_id: str) -> bytes | None:
        with self._lock:
            cached = self._model_cache.get(task_id)
        if cached is not None:
            return cached

        with self._lock:
            url = self._download_urls.get(task_id)
        if url is None:
            return None

        download = self._client.get(url, follow_redirects=True)
        download.raise_for_status()
        glb = download.content
        with self._lock:
            self._model_cache[task_id] = glb
        return glb
