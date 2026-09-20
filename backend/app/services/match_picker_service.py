"""Picking the best Poly Pizza search result for a filler keyword.

library_asset_service.py's PolyPizzaLibraryAssetService fetches a page of
raw search results for a keyword and needs to pick (at most) one. Two ways
to do that ship, selected by MATCH_PICKER_PROVIDER:

- ClaudeMatchPickerService (default, MATCH_PICKER_PROVIDER=claude): one
  Claude call per keyword, given the keyword plus every candidate's
  title/tags/category, asking it to semantically pick the best one (or
  none). This is what lets compound keywords work correctly without
  narrowing what Claude is allowed to suggest as a keyword in the first
  place. Requires ANTHROPIC_API_KEY.
- HeuristicMatchPickerService (MATCH_PICKER_PROVIDER=heuristic, no
  network/key needed): scores each result's title by how many of the
  keyword's words appear in it as a substring. Free and fast, but
  structurally can't handle a compound keyword whose correct match uses
  different words entirely (keyword "clock_tower" vs. a real result titled
  "Bell Tower" -- zero words in common) or a genuine synonym (keyword
  "bicycle_rack" vs. "Bike Rack" -- "bicycle" never appears). See its
  docstring for the full picture. It remains the zero-dependency default
  used when a PolyPizzaLibraryAssetService is constructed without an
  explicit match_picker (e.g. direct/test construction) -- only
  providers.build_from_env()'s MATCH_PICKER_PROVIDER default is "claude".

Both return the winning candidate's `id`, or None if nothing is a
confident match -- exactly like a "no results" search, the caller just
drops the object.
"""

from __future__ import annotations

import logging
from abc import ABC, abstractmethod

from pydantic import BaseModel, Field

logger = logging.getLogger(__name__)


class MatchCandidate(BaseModel):
    id: str
    title: str
    tags: list[str] = Field(default_factory=list)
    category: str | None = None


class MatchPickerService(ABC):
    @abstractmethod
    def pick(self, keyword: str, candidates: list[MatchCandidate]) -> str | None:
        """Return the id of whichever candidate best matches keyword, or None
        if nothing is a confident enough match to use."""
        raise NotImplementedError


def _heuristic_score(keyword: str, candidate: MatchCandidate) -> int:
    keyword_words = [w for w in keyword.lower().replace("_", " ").split() if w]
    title_lower = candidate.title.lower()
    return sum(1 for word in keyword_words if word in title_lower)


class HeuristicMatchPickerService(MatchPickerService):
    """Zero-cost default: score every candidate by how many of the keyword's
    words appear as a substring in its title (so "street" also matches the
    compound title "Streetlight"), and take the highest-scoring one, breaking
    ties by the caller's own candidate order. A score of 0 everywhere means
    no confident match.

    This alone fixed real bugs seen live (Poly Pizza's #1 result for
    "trash_can" was "Debris Papers"; for "street_lamp" it was "Road Bits" --
    blindly taking result[0] silently attaches the wrong mesh), but it has a
    hard ceiling: it can only ever pick among candidates that share a literal
    word with the keyword, so a compound keyword like "clock_tower" with no
    "clock tower" result at all just picks whichever single word (clock, or
    tower) happens to match something -- it can't recognize that "Bell
    Tower" is the right kind of object despite sharing zero words. That gap
    is exactly what ClaudeMatchPickerService is for.
    """

    def pick(self, keyword: str, candidates: list[MatchCandidate]) -> str | None:
        best: MatchCandidate | None = None
        best_score = 0
        for candidate in candidates:
            score = _heuristic_score(keyword, candidate)
            if score > best_score:
                best_score = score
                best = candidate
        return best.id if best else None


_CLAUDE_MODEL = "claude-haiku-4-5-20251001"


class ClaudeMatchPickerService(MatchPickerService):
    """Real picker: one Claude call per keyword, asking it to semantically
    choose the best-matching candidate (or none) from title/tags/category
    alone. Uses Haiku rather than the Opus model the rest of this pipeline
    uses for extraction -- this is a small, fast classification decision
    over a short list, not open-ended understanding of an image or
    description, and it can run once per filler keyword (up to
    MAX_FILLER_ASSETS_PER_WORLD times per request), so latency/cost matter
    more here than anywhere else in the pipeline.

    Reads ANTHROPIC_API_KEY (or an `ant auth login` profile) from the
    environment via the SDK's default client, same as every other Claude
    service in this codebase.
    """

    def __init__(self, client=None):
        import anthropic  # imported lazily so the heuristic default never needs the SDK

        self._client = client or anthropic.Anthropic()

    def pick(self, keyword: str, candidates: list[MatchCandidate]) -> str | None:
        if not candidates:
            return None

        from app.prompts.match_picker import SYSTEM_PROMPT, build_user_prompt

        response = self._client.beta.messages.parse(
            model=_CLAUDE_MODEL,
            max_tokens=200,
            system=SYSTEM_PROMPT,
            messages=[{"role": "user", "content": build_user_prompt(keyword, candidates)}],
            output_format=_MatchChoice,
            betas=["server-side-fallback-2026-07-01"],
            fallbacks="default",
        )

        if response.stop_reason == "refusal" or response.parsed_output is None:
            logger.warning("match picker: no result for keyword %r (stop_reason=%s)", keyword, response.stop_reason)
            return None

        choice = response.parsed_output
        if choice.chosen_index is None:
            logger.info("match picker: Claude found no confident match for keyword %r (%s)", keyword, choice.reason)
            return None
        if not (0 <= choice.chosen_index < len(candidates)):
            logger.warning(
                "match picker: Claude returned out-of-range index %d for keyword %r (%d candidates); treating as no match",
                choice.chosen_index,
                keyword,
                len(candidates),
            )
            return None

        chosen = candidates[choice.chosen_index]
        logger.info("match picker: %r -> %r (%s)", keyword, chosen.title, choice.reason)
        return chosen.id


class _MatchChoice(BaseModel):
    chosen_index: int | None = Field(
        default=None,
        description="0-based index into the candidates list of the best match, or null if none match.",
    )
    # No max_length: structured-output generation only enforces shape/type,
    # not string length, so a hard cap here would make an over-long (but
    # otherwise valid) response fail Pydantic validation entirely, turning a
    # verbose explanation into a crashed match instead of just a long log
    # line. This field is logging-only, so there's nothing to protect.
    reason: str = Field(description="One short sentence explaining the choice.")
