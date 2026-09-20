"""Final composition merge for the personalize=true pipeline.

object_extraction.py, text_asset_extraction.py, and filler_asset_extraction.py
(see app.prompts) each run as independent Claude calls with no visibility
into what the other two propose -- so in personalize=true mode the combined
pool can end up with several competing "landmark" candidates from different
sources, reconciled only by world_synthesis_service's naive first-come-
first-served demotion (photo objects arbitrated first, then text, then
filler, capped at MAX_LANDMARKS along the way). This service makes one more
Claude call, after all three extractions finish and before any mesh/library
submission, to decide the actual final composition instead: at most two
"landmark"s, everything else roadside/scattered -- reasoning about which
candidates deserve the scarce slot rather than just taking whichever source
happened to run first. It can also drop a candidate outright (e.g. a
near-duplicate); running this before submission means a dropped candidate
never costs a wasted Meshy/library call.

"background" is NOT decided here: generate_world.py's _merge_assets
hardcodes it to always be filler's own background candidate (guaranteed to
exist -- see filler_asset_extraction's prompt) before this service is ever
called, demoting any photo/text "background" candidate to "scattered" and
excluding filler's background candidate from the pool entirely. A
MergeCandidate/MergeDecision can technically still carry "background" (the
Placement type is shared), but in practice this service never sees or
returns one when called through the normal pipeline.

Only used when personalize=True: personalize=False sources the entire world
from filler alone (see docs/decisions/0012-personalize-toggle.md), which
already stages its own single background/landmark/fill-the-rest composition
in one call, so there's nothing to merge across sources.

- MockAssetMergeService (default, no network): keeps every candidate with
  its own originally-suggested placement, unchanged -- a safe no-op so an
  offline demo still produces a world, just without this call's curation
  (world_synthesis_service's own demotion/truncation logic remains the
  backstop either way).
- ClaudeAssetMergeService (ASSET_MERGE_PROVIDER=claude): one structured-
  output Claude call over the combined candidate list.
"""

from __future__ import annotations

import logging
from abc import ABC, abstractmethod
from typing import Literal

from pydantic import BaseModel, Field

from app.services.vision_service import Placement
from app.services.world_synthesis_service import MAX_LANDMARKS

logger = logging.getLogger(__name__)

MergeSource = Literal["photo", "text", "filler"]


class MergeCandidate(BaseModel):
    """One candidate object from one of the three extraction sources,
    flattened to just what the merge decision needs.

    `index` is unique across the whole combined list passed to a single
    merge() call, and is the only thing the model's response references
    back (see MergeDecision) -- the same "pick by index, never ask the
    model to echo data back" pattern as app.services.match_picker_service.
    """

    index: int
    source: MergeSource
    label: str
    placement: Placement
    density: float


class MergeDecision(BaseModel):
    index: int
    keep: bool = True
    placement: Placement = "scattered"


class AssetMergeResult(BaseModel):
    decisions: list[MergeDecision] = Field(default_factory=list)


class AssetMergeService(ABC):
    @abstractmethod
    def merge(self, candidates: list[MergeCandidate], max_assets: int) -> AssetMergeResult:
        """Decide the final keep/placement for every candidate in one shot.
        `max_assets` (providers.max_assets_per_world) is a hint for how
        aggressively to drop candidates, not a hard requirement the caller
        depends on -- world_synthesis_service truncates again downstream
        regardless."""
        raise NotImplementedError


class MockAssetMergeService(AssetMergeService):
    def merge(self, candidates: list[MergeCandidate], max_assets: int) -> AssetMergeResult:
        return AssetMergeResult(
            decisions=[MergeDecision(index=c.index, keep=True, placement=c.placement) for c in candidates]
        )


_CLAUDE_MODEL = "claude-haiku-4-5-20251001"


class ClaudeAssetMergeService(AssetMergeService):
    """Real merge: one Claude call over the combined candidate list. Haiku,
    not Opus (unlike the three extraction calls): this is a classification/
    reconciliation decision over a short list Claude already has full
    context for, not open-ended photo/text understanding.
    """

    def __init__(self, client=None):
        import anthropic  # imported lazily so mock mode never needs the SDK

        self._client = client or anthropic.Anthropic()

    def merge(self, candidates: list[MergeCandidate], max_assets: int) -> AssetMergeResult:
        if not candidates:
            return AssetMergeResult(decisions=[])

        from app.prompts.asset_merge import SYSTEM_PROMPT, build_user_prompt

        response = self._client.beta.messages.parse(
            model=_CLAUDE_MODEL,
            max_tokens=2048,
            system=SYSTEM_PROMPT.format(max_assets=max_assets),
            messages=[{"role": "user", "content": build_user_prompt(candidates)}],
            output_format=AssetMergeResult,
            betas=["server-side-fallback-2026-07-01"],
            fallbacks="default",
        )

        if response.stop_reason == "refusal" or response.parsed_output is None:
            raise RuntimeError(f"asset merge did not return a result (stop_reason={response.stop_reason})")

        return _sanitize(candidates, response.parsed_output)


def _sanitize(candidates: list[MergeCandidate], result: AssetMergeResult) -> AssetMergeResult:
    """Defense in depth: the prompt asks for exactly one decision per
    candidate and caps "landmark" count itself, but a structured-output call
    is never trusted alone for a hard invariant a creative response could
    violate. A candidate missing from the response defaults to kept,
    placement unchanged; a third "landmark" is demoted to "scattered",
    first-decided-first-kept (input order, i.e. photo before text before
    filler). The "background" guard here is pure backstop -- the prompt
    tells the model it will never see one and generate_world.py's
    _merge_assets never sends one -- but is kept in case that ever changes
    or this service is called some other way."""
    by_index = {d.index: d for d in result.decisions}
    background_used = False
    landmark_count = 0
    decisions: list[MergeDecision] = []
    for candidate in candidates:
        decision = by_index.get(candidate.index)
        if decision is not None and not decision.keep:
            continue
        placement = decision.placement if decision is not None else candidate.placement

        if placement == "background":
            if background_used:
                placement = "scattered"
            else:
                background_used = True
        elif placement == "landmark":
            if landmark_count >= MAX_LANDMARKS:
                placement = "scattered"
            else:
                landmark_count += 1

        decisions.append(MergeDecision(index=candidate.index, keep=True, placement=placement))
    return AssetMergeResult(decisions=decisions)
