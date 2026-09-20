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

_BASE_PROMPT = """\
You look at a player's photo and text description for a 2-player kart racing \
game whose environment is generated from them. {objects_context}

Produce up to {max_assets} filler_assets, each:
- keyword: a short, SIMPLE, GENERIC search term (1-2 common words, snake_case) \
that a public 3D model library would likely have results for -- e.g. \
"traffic_cone", "park_bench", "wooden_crate", "trash_can"{landmark_keyword_example}. \
Prefer boring, common nouns over anything specific, branded, or unusual: a \
keyword too narrow or unusual will return no search results and the object \
will simply be dropped. Prefer a SINGLE concept over combining two nouns into \
one keyword (e.g. "tower" or "bike_rack" rather than "clock_tower" or \
"bicycle_rack") -- a library search on a two-concept keyword often just \
matches one of the two words and returns the wrong kind of object entirely \
(a plain desk clock instead of a tower; a bicycle instead of a rack).
- density: 0.0 to 1.0. What it actually controls depends on placement: for \
"roadside" it sets the spacing between instances (higher = closer together); \
for "scattered" it sets how many clustered copies appear (higher = more). \
For "landmark"/"background" the instance count is fixed by the game itself, \
so a low value such as 0.1-0.2 is fine there.
- placement: how the game should use this object, exactly one of \
"roadside" (lines a path at intervals: cones, bollards, benches, lamps), \
"background" (distant skyline shapes), "scattered" (casual clutter in \
groups: rocks, crates, bushes){landmark_placement_option}.

Rules:
{landmark_rule}- Suggest props that plausibly fit the scene's theme/setting, but keep every \
keyword itself generic and common, never unique or descriptive.
- Only static decoration, matching the same rules as the other extraction \
steps: never people, animals, text, logos, hazards, items, or gameplay \
mechanics.
- If the scene already feels complete, or nothing generic and fitting comes \
to mind, return an empty filler_assets list rather than inventing filler \
that doesn't fit."""


def build_system_prompt(max_assets: int, max_landmarks: int = 0) -> str:
    if max_landmarks > 0:
        objects_context = (
            "No other step in this pipeline is generating custom meshes for this world right now, so "
            "you are the ONLY source of decoration -- populate the whole scene. First look at what's "
            "actually visible in the photo and what's named in the text description, and for each, "
            "suggest the closest GENERIC, common search term a public 3D asset library would likely "
            "have (an ornate iron streetlamp in the photo becomes \"street_lamp\"; a fountain mentioned "
            "in the text becomes \"fountain\") -- never the specific/unique version, just a plausible "
            "generic stand-in. Then, once you've covered what's actually there, round out the rest with "
            "additional generic decoration props that would plausibly exist in a public 3D asset "
            "library and fit the scene's theme (rocks, benches, traffic cones, crates, fences, trash "
            "cans, park lamps)."
        )
        landmark_keyword_example = ', or, for a landmark, "lighthouse", "fountain", "tower"'
        landmark_placement_option = (
            f', or "landmark" (at most {max_landmarks}: the visual centrepiece, placed once or twice, '
            "oversized, at key points of the track -- ONLY for something that is actually visible in "
            "the photo or actually named in the text, translated into its closest generic library "
            "term; never invent a landmark that isn't grounded in what's really there)"
        )
        landmark_rule = (
            f"- \"landmark\" is ONLY for something actually visible in the photo or actually named in "
            "the text (translated into its closest generic, single-concept library term) -- e.g. if the "
            "photo shows a real statue, \"statue\" may be a landmark; never invent a landmark that isn't "
            "grounded in what's really there. It is normal, expected, and fine for NO object to be "
            f"\"landmark\" if nothing observed or mentioned could plausibly serve as one -- do not force "
            f"one just because up to {max_landmarks} are allowed. This restriction applies ONLY to "
            "\"landmark\": every other object (\"roadside\"/\"background\"/\"scattered\") may still be "
            "freely invented generic filler to round out the world, whether or not it relates to "
            "anything in the photo or text.\n"
        )
    else:
        objects_context = (
            "Separate steps already turn specific objects -- both what's visible in the photo and "
            "distinctive things named in the text -- into unique custom-generated meshes. Your job is "
            "different: suggest GENERIC, common decoration props that would plausibly exist in a public "
            "3D asset library, to help fill out and diversify the world with plausible variety (rocks, "
            "benches, traffic cones, crates, fences, trash cans, park lamps) -- not anything unique or "
            "distinctive enough to deserve its own custom mesh."
        )
        landmark_keyword_example = ""
        landmark_placement_option = ""
        landmark_rule = (
            "- Never \"landmark\" -- filler is generic, interchangeable clutter by definition, never a "
            "centrepiece.\n"
        )

    return _BASE_PROMPT.format(
        objects_context=objects_context,
        max_assets=max_assets,
        landmark_keyword_example=landmark_keyword_example,
        landmark_placement_option=landmark_placement_option,
        landmark_rule=landmark_rule,
    )


def build_user_prompt(description: str) -> str:
    return (
        f"Player description of the world they want: {description!r}\n"
        "Look at the attached photo and suggest generic filler props, if any fit."
    )
