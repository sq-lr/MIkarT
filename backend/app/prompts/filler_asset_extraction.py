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

The prompt also asks for one scene-level property outside the filler_assets
list: `ground_color`, a hex color for the grass/ground verge alongside the
track, matching the theme (see FillerAssetExtraction.ground_color -- not yet
wired into WorldRecipe/Unity, just captured here for whatever consumes it
next).

Beyond that, it asks for exactly three things in order, not one big undirected
list: exactly one "background" asset (always -- see EnvironmentGenerator.
PlaceBackgroundWall: it's a continuous, close, prominent wall lining both
sides of the whole track now, not a distant skyline, so Claude is told
that explicitly rather than left to guess from a generic placement
description), then 0-2 "landmark" assets (only when max_landmarks > 0 --
see below), then whatever's left filled with "roadside"/"scattered"
clutter. Asking directly for the background/landmark slots first, with
their real in-game meaning spelled out, produces far more deliberate
choices than leaving them to compete for attention inside one undifferentiated
list of up to a dozen items.

`max_landmarks` is normally 0 (see docs/decisions/0012-personalize-toggle.md):
filler is generic clutter, never a centrepiece, and this call only tops up
whatever the (separate) photo/text-to-Meshy steps already produced. It's only
raised when the "generate personalized assets" toggle is off, since in that
mode this is the ONLY source of objects in the world at all -- so the prompt
also shifts from pure generic clutter to first translating whatever's
actually observed in the photo or named in the text into generic,
library-searchable equivalents (an ornate iron streetlamp in the photo still
becomes the keyword "street_lamp"), before falling back to unrelated generic
filler to round out the rest. "landmark" specifically may only be assigned to
something grounded in the photo/text this way -- never invented -- so it's
normal for a world to end up with zero landmarks; every other placement may
still be freely invented regardless of what's actually in the photo/text.
"""

from __future__ import annotations

_KEYWORD_FORMAT_RULES = """\
Every keyword (in any of the three steps below) must be a short, SIMPLE, \
GENERIC search term (1-2 common words, snake_case) that a public 3D model \
library would likely have results for -- e.g. "traffic_cone", "park_bench", \
"wooden_crate", "mountain", "pine_tree". Prefer boring, common nouns over \
anything specific, branded, or unusual: a keyword too narrow or unusual will \
return no search results and the object will simply be dropped. Prefer a \
SINGLE concept over combining two nouns into one keyword (e.g. "tower" or \
"bike_rack" rather than "clock_tower" or "bicycle_rack") -- a library search \
on a two-concept keyword often just matches one of the two words and returns \
the wrong kind of object entirely (a plain desk clock instead of a tower; a \
bicycle instead of a rack)."""

_BASE_PROMPT = """\
You look at a player's photo and text description for a 2-player kart \
racing game whose environment is generated from them. {objects_context}

{keyword_format_rules}

GROUND COLOR -- also output ground_color: a single hex color (e.g. \
"#4a7c3a") for the ground/grass verge running alongside the track, matching \
the scene's theme. Examples: a lush jungle or forest -> a deep saturated \
green like "#2f5233"; a sun-baked desert -> a warm sandy tan like "#d9b877"; \
a snowy/alpine scene -> a pale blue-white like "#e8eef2"; a beach -> a light \
sandy tan like "#e3c98f"; an autumn scene -> a warm brown-orange like \
"#8a5a2e"; an urban/city scene -> a neutral grey-green like "#6b7a63". Pick \
whichever single color best fits the photo and description, even if none of \
these examples match exactly.

Answer the following in order, producing up to {max_assets} filler_assets \
in total:

1. BACKGROUND -- exactly one filler_asset with placement="background". This \
isn't a distant skyline: it's a continuous, close, deliberately prominent \
wall of copies lining BOTH sides of the entire track, overlapping itself, \
that the road cuts straight through and that blocks the view almost \
immediately. Based on the photo and the text, what should that wall \
actually be made of? Common examples: "mountain", "cliff", "hill", \
"pine_tree" (a dense treeline), "palm_tree" (a jungle wall), "building" (a \
city skyline pressed close), "dune", "wall". Pick whichever single keyword best \
fits the scene's setting and give it a low density (0.1-0.2 -- the instance \
count along the wall is fixed by the game itself, density barely matters \
here).
{landmark_step}
{fill_step}

Rules:
- Suggest props that plausibly fit the scene's theme/setting, but keep \
every keyword itself generic and common, never unique or descriptive.
- Only static decoration, matching the same rules as the other extraction \
steps: never people, animals, text, logos, hazards, items, or gameplay \
mechanics.
- If truly nothing generic and fitting comes to mind for a roadside/scattered \
slot, it's fine to leave it unfilled rather than inventing filler that \
doesn't fit -- but always produce the one background asset."""


def build_system_prompt(max_assets: int, max_landmarks: int = 0) -> str:
    remaining_after_background = max(0, max_assets - 1)

    if max_landmarks > 0:
        objects_context = (
            "No other step in this pipeline is generating custom meshes for this world right now, so "
            "you are the ONLY source of decoration -- populate the whole scene."
        )
        remaining_slots = max(0, remaining_after_background - max_landmarks)
        landmark_step = f"""
2. LANDMARK -- up to {max_landmarks} filler_asset(s) with placement="landmark", the oversized \
centrepiece(s) placed once or twice at key points of the track. This one has a hard requirement: \
it must ideally be something actually visible in the photo, or something explicitly named in the \
text -- e.g. if the photo shows a real statue, suggest "statue"; if the text says "with a lighthouse", \
suggest "lighthouse". Translate whatever it is into its closest generic, single-concept library term. \
Do NOT invent a landmark that isn't grounded in what's really there -- if nothing in the photo or text \
plausibly serves as a centrepiece, it is normal and expected to suggest zero landmarks; never force one \
just because up to {max_landmarks} are allowed. Give each a low density (0.1-0.2)."""
        fill_step = f"""
3. FILL THE REST -- up to {remaining_slots} more filler_assets with placement="roadside" or \
"scattered" (never "landmark", never a second "background"). First look at what else is actually \
visible in the photo or named in the text, and for each, suggest the closest generic, common search \
term (an ornate iron streetlamp in the photo becomes "street_lamp"; a fountain mentioned in the text \
becomes "fountain") -- never the specific/unique version, just a plausible generic stand-in. Then, \
once you've covered what's actually there, round out the rest with additional generic decoration props \
that would plausibly exist in a public 3D asset library and fit the scene's theme (rocks, benches, \
traffic cones, crates, fences, trash cans, park lamps). "roadside" lines a path at intervals (cones, \
bollards, benches, lamps); "scattered" is casual clutter in groups (rocks, crates, bushes). density: \
for "roadside" it sets the spacing between instances (higher = closer together); for "scattered" it \
sets how many clustered copies appear (higher = more)."""
    else:
        objects_context = (
            "Separate steps already turn specific objects -- both what's visible in the photo and "
            "distinctive things named in the text -- into unique custom-generated meshes. Your job here "
            "is different: generic, common decoration to help fill out and diversify the world with "
            "plausible variety, not anything unique or distinctive enough to deserve its own custom mesh."
        )
        landmark_step = ""
        remaining_slots = remaining_after_background
        fill_step = f"""
2. FILL THE REST -- up to {remaining_slots} more filler_assets with placement="roadside" or \
"scattered" (never "landmark" -- filler is generic, interchangeable clutter by definition, never a \
centrepiece; never a second "background"). Suggest generic, common decoration props that would \
plausibly exist in a public 3D asset library and fit the scene's theme (rocks, benches, traffic cones, \
crates, fences, trash cans, park lamps) -- not anything unique or distinctive enough to deserve its own \
custom mesh. "roadside" lines a path at intervals (cones, bollards, benches, lamps); "scattered" is \
casual clutter in groups (rocks, crates, bushes). density: for "roadside" it sets the spacing between \
instances (higher = closer together); for "scattered" it sets how many clustered copies appear (higher \
= more)."""

    return _BASE_PROMPT.format(
        objects_context=objects_context,
        keyword_format_rules=_KEYWORD_FORMAT_RULES,
        max_assets=max_assets,
        landmark_step=landmark_step,
        fill_step=fill_step,
    )


def build_user_prompt(description: str) -> str:
    return (
        f"Player description of the world they want: {description!r}\n"
        "Look at the attached photo and answer the background/landmark/fill-the-rest steps in order."
    )
