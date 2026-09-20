"""Prompt text for ClaudeMatchPickerService: picking the best Poly Pizza
search result for a filler keyword.

Distinct from every other prompt module in this pipeline: this one doesn't
extract anything from a photo or description, it's a small classification
call over already-fetched library search results (app.services.library_asset_service),
letting a compound keyword like "clock_tower" correctly match a title like
"Bell Tower" even though they share no literal substring -- something no
string-scoring heuristic can do (see HeuristicMatchPickerService's docstring
for what that heuristic can and can't cover).
"""

from __future__ import annotations

from app.services.match_picker_service import MatchCandidate

SYSTEM_PROMPT = """\
You are matching a generic search keyword to the single best result from a \
public 3D model library's search results, for use as roadside/background/\
scattered decoration in a kart racing game.

Given the keyword and a numbered list of candidate assets (each with a \
title, tags, and category), choose whichever ONE candidate best represents \
the kind of object the keyword describes. A candidate does not need to \
contain the keyword's exact words -- a semantically equivalent object is a \
good match (e.g. keyword "clock_tower" matches a candidate titled "Bell \
Tower" or "Watch Tower"; keyword "bicycle_rack" matches "Bike Rack").

If NONE of the candidates plausibly represent the keyword's object, respond \
with no chosen candidate rather than forcing a bad match -- attaching the \
wrong mesh (e.g. a scrap of paper for "trash_can") is worse than skipping \
the object entirely."""


def build_user_prompt(keyword: str, candidates: list[MatchCandidate]) -> str:
    lines = [f"Keyword: {keyword!r}", "", "Candidates:"]
    for i, candidate in enumerate(candidates):
        tags = ", ".join(candidate.tags) if candidate.tags else "none"
        category = candidate.category or "none"
        lines.append(f"{i}. title={candidate.title!r} tags=[{tags}] category={category!r}")
    lines.append("")
    lines.append("Which numbered candidate best matches the keyword? Say none if none do.")
    return "\n".join(lines)
