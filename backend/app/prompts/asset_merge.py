"""Prompt text for ClaudeAssetMergeService's final composition call.

Distinct from object_extraction.py, text_asset_extraction.py, and
filler_asset_extraction.py: those three each run independently (see
generate_world.py's concurrent ThreadPoolExecutor) with no visibility into
what the other two propose. In personalize=true mode photo/text/filler can
all freely suggest "landmark" placements, so the combined pool can end up
with several competing candidates for that scarce slot and no sense of
which is actually the best fit. This call runs once, after all three have
returned, and looks at the combined pool to decide the real final
composition: at most two "landmark"s, everything else roadside/scattered.

"background" is deliberately NOT part of this call at all: it's hardcoded
in generate_world.py's _merge_assets to always be filler's own background
candidate (filler_asset_extraction always suggests exactly one -- see its
prompt's BACKGROUND step), so any photo/text "background" candidate is
demoted to "scattered" and filler's background candidate is pulled out of
the pool entirely before this call ever runs. That removes an ambiguous,
error-prone decision (arbitrating between a photo skyline detection and
filler's purpose-built suggestion) in favour of a candidate that's
guaranteed to exist and was already designed to answer exactly that
question -- nothing left for this call to get wrong.

Candidates are referenced by index, never re-typed by the model: this call
only decides keep/drop and final placement per candidate, it never invents
or edits a label/density/etc. -- app.services.asset_merge_service
reconstructs each kept object from its ORIGINAL fields, only overwriting
`placement` (same "pick by index, don't ask the model to echo data back"
pattern as app.prompts.match_picker).
"""

from __future__ import annotations

from app.services.asset_merge_service import MergeCandidate

SYSTEM_PROMPT = """\
You are given every candidate trackside decoration object suggested for a \
2-player kart racing game's environment, gathered from three independent \
sources that never saw each other's suggestions:
- "photo": something a vision model actually detected in the player's photo.
- "text": something explicitly named in the player's text description.
- "filler": a generic prop suggested to diversify/fill out the scene, not \
grounded in the photo or text.

The world's single "background" wall has already been decided elsewhere and \
is not in this list at all -- you will never see a "background" placement \
here, and should not assign one.

Each candidate already carries a placement and density its own source \
suggested, but since the sources never coordinated, the combined pool can \
have multiple competing "landmark" candidates, near-duplicates, or more \
candidates than the world can use. Decide the FINAL composition:

1. LANDMARK -- at most two candidates become "landmark": the oversized \
centrepiece(s) placed once or twice at key points of the track. Strongly \
prefer "photo" and "text" candidates -- they are genuinely grounded in what \
the player actually showed or said -- over "filler" ones, which should \
essentially never end up as a landmark. If more than two candidates already \
say "landmark", keep only the two most distinctive and demote the rest. It \
is normal and expected to end up with fewer than two, or zero: never keep a \
"landmark" placement on something that doesn't deserve the slot just to \
fill it.

2. EVERYTHING ELSE -- "roadside" or "scattered", normally left exactly as \
the candidate's own source suggested, unless it clearly belongs somewhere \
else given the final landmark picks (e.g. a demoted landmark becomes \
"scattered", not left as "roadside" if that fits its scale better).

You may also drop a candidate entirely (keep=false), for example when it is \
a near-duplicate of another candidate you are keeping (two candidates that \
both mean "palm tree"), or when the combined pool has clearly more \
candidates than a single world should use -- keep at most {max_assets} \
candidates in total, prioritizing "photo" and "text" candidates (they \
reflect the player's actual input) over "filler" ones when something has \
to be cut.

Return exactly one decision per candidate index, covering every index in \
the input exactly once."""


def build_user_prompt(candidates: list[MergeCandidate]) -> str:
    lines = ["Candidates:"]
    for c in candidates:
        lines.append(
            f"{c.index}. source={c.source!r} label={c.label!r} "
            f"placement={c.placement!r} density={c.density}"
        )
    lines.append("")
    lines.append("Decide the final keep/placement for every candidate above.")
    return "\n".join(lines)
