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
  keyword search (https://poly.pizza/docs/api/v1.1). A search returns a page
  of loosely-ranked candidates, not one exact hit, so picking which one (if
  any) to use is delegated to a MatchPickerService (see
  app.services.match_picker_service) rather than done here.
"""

from __future__ import annotations

import logging
import threading
from abc import ABC, abstractmethod

import httpx
from pydantic import BaseModel

from app.services.filler_asset_service import FillerAsset
from app.services.match_picker_service import HeuristicMatchPickerService, MatchCandidate, MatchPickerService
from app.services.mesh_generation_service import MeshTask, MeshTaskStatus

logger = logging.getLogger(__name__)


class LibraryAssetService(ABC):
    @abstractmethod
    def submit(self, asset: FillerAsset, exclude_ids: frozenset[str] = frozenset()) -> MeshTask | None:
        """Search the library for asset.keyword. Returns None if nothing
        matched (the object is simply dropped -- unlike Meshy, there's no
        generation fallback) or this provider has no library (mock); raises
        on a real provider error.

        `exclude_ids` skips those result IDs before matching -- used to pull
        a second, genuinely different match for the same keyword (see
        generate_world._submit_filler_assets's background-wall variety),
        rather than returning the same result again."""
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

    def submit(self, asset: FillerAsset, exclude_ids: frozenset[str] = frozenset()) -> MeshTask | None:
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
    Tags: list[str] = []
    Category: str | None = None


class PolyPizzaLibraryAssetService(LibraryAssetService):
    """Poly Pizza (https://poly.pizza/docs/api/v1.1): a free library of
    CC0/CC-BY low-poly models delivered as glTF -- the same format Meshy
    returns, so Unity's glTFast import path is unchanged.

    submit  -> GET /search/{keyword}, pick the best-matching result (see
               a MatchPickerService), resolve (but don't yet download) its
               GLB url
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
        match_picker: MatchPickerService | None = None,
    ):
        if not api_key:
            raise ValueError("POLYPIZZA_API_KEY is required for LIBRARY_PROVIDER=polypizza")
        self._client = httpx.Client(
            base_url=base_url,
            headers={"X-Auth-Token": api_key},
            timeout=timeout,
            transport=transport,
        )
        self._match_picker = match_picker or HeuristicMatchPickerService()
        self._download_urls: dict[str, str] = {}
        self._model_cache: dict[str, bytes] = {}
        self._lock = threading.Lock()

    def submit(self, asset: FillerAsset, exclude_ids: frozenset[str] = frozenset()) -> MeshTask | None:
        response = self._client.get(f"/search/{asset.keyword}")
        response.raise_for_status()
        body = response.json()
        results = [r for r in (body.get("results") or []) if r["ID"] not in exclude_ids]
        results_by_id = {r["ID"]: r for r in results}
        candidates = [
            MatchCandidate(id=r["ID"], title=r["Title"], tags=r.get("Tags") or [], category=r.get("Category"))
            for r in results
        ]
        chosen_id = self._match_picker.pick(asset.keyword, candidates)
        if chosen_id is None or chosen_id not in results_by_id:
            logger.info("poly pizza: no confident match for keyword %r (%d raw results)", asset.keyword, len(results))
            return None

        result = _SearchResult.model_validate(results_by_id[chosen_id])
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
