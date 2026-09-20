"""Prompt text for ClaudeFillerAssetService's photo+text call.

Distinct from object_extraction.py and text_asset_extraction.py: those two
find SPECIFIC objects (in the photo, or vividly named in the description)
and generate a unique mesh for each. This one looks at the whole scene and
suggests GENERIC, common props to search a public 3D asset library for
(Poly Pizza) -- the goal is diversifying and filling out the world with
plausible clutter, not personalizing it. Each suggested `keyword` is sent
directly to that library's keyword search by
app.services.library_asset_service, so it must be a simple, common search
term, not a vivid description.
"""

from __future__ import annotations

SYSTEM_PROMPT = """\
You look at a player's photo and text description for a 2-player kart racing \
game whose environment is generated from them. Separate steps already turn \
specific objects -- both what's visible in the photo and distinctive things \
named in the text -- into unique custom-generated meshes. Your job is \
different: suggest GENERIC, common decoration props that would plausibly \
exist in a public 3D asset library, to help fill out and diversify the world \
with plausible variety (rocks, benches, traffic cones, crates, fences, \
trash cans, park lamps) -- not anything unique or distinctive enough to \
deserve its own custom mesh.

Produce up to {max_assets} filler_assets, each:
- keyword: a short, SIMPLE, GENERIC search term (1-2 common words, snake_case) \
that a public 3D model library would likely have results for -- e.g. \
"traffic_cone", "park_bench", "wooden_crate", "trash_can". Prefer boring, \
common nouns over anything specific, branded, or unusual: a keyword too \
narrow or unusual will return no search results and the object will simply \
be dropped.
- density: 0.0 to 1.0. What it actually controls depends on placement: for \
"roadside" it sets the spacing between instances (higher = closer together); \
for "scattered" it sets how many clustered copies appear (higher = more). \
For "background" the instance count is fixed by the game itself, so a low \
value such as 0.1-0.2 is fine there.
- placement: how the game should use this object, exactly one of \
"roadside" (lines a path at intervals: cones, bollards, benches, lamps), \
"background" (distant skyline shapes), or "scattered" (casual clutter in \
groups: rocks, crates, bushes). Never "landmark" -- filler is generic, \
interchangeable clutter by definition, never a centrepiece.

Rules:
- Suggest props that plausibly fit the scene's theme/setting, but keep every \
keyword itself generic and common, never unique or descriptive.
- Only static decoration, matching the same rules as the other extraction \
steps: never people, animals, text, logos, hazards, items, or gameplay \
mechanics.
- If the scene already feels complete, or nothing generic and fitting comes \
to mind, return an empty filler_assets list rather than inventing filler \
that doesn't fit."""


def build_user_prompt(description: str) -> str:
    return (
        f"Player description of the world they want: {description!r}\n"
        "Look at the attached photo and suggest generic filler props, if any fit."
    )
