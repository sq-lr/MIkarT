"""Prompt text for ClaudeTextAssetService's text-only extraction call.

Complementary to object_extraction.py: that prompt only ever describes what a
VLM can see in the photo. This one reads the player's text description alone
and pulls out distinctive props the *words* call for -- things a photo of an
arbitrary place wouldn't already imply. Each extracted asset's `prompt` is
sent directly to Meshy Text-to-3D by app.services.mesh_generation_service.
"""

from __future__ import annotations

SYSTEM_PROMPT = """\
You read a player's short text description of the world they want for a \
2-player kart racing game. A separate step already turns objects VISIBLE IN \
THEIR PHOTO into 3D props -- your job is different: pull out distinctive, \
nameable props the DESCRIPTION calls for that an arbitrary photo would not \
already imply (e.g. "a giant floating whale statue", "a dragon fountain", \
"neon vending machines"), not generic mood or terrain words (e.g. "chaotic", \
"tropical", "snowy") that are already handled elsewhere by theme/terrain/\
palette selection.

Produce up to {max_assets} key_assets, each:
- label: a short snake_case noun such as "whale_statue", "dragon_fountain", \
"vending_machine".
- prompt: a vivid, self-contained visual description (max 800 characters) of \
this object's shape, materials, color, and style -- suitable as the ONLY \
context given to a text-to-3D model generator. Never mention the racing \
game, camera angles, scene framing, or gameplay behavior -- describe the \
object itself, as if photographing it in isolation.
- density: 0.0 to 1.0, how much this object should populate the world. What \
it actually controls depends on placement (see below): for "roadside" it sets \
the spacing between instances (higher = closer together); for "scattered" it \
sets how many clustered copies appear (higher = more). For "landmark" and \
"background" the instance count is fixed by the game itself, so a low value \
such as 0.1-0.2 is fine there.
- placement: how the game should use this object, exactly one of:
    "landmark"   -- the visual centrepiece the description is built around \
(a statue, a fountain, one big distinctive structure). Placed once or twice, \
oversized, at key points of the track.
    "roadside"   -- things that would line a path or road at regular \
intervals (lamp posts, fences, stalls, vending machines, planters).
    "background" -- large distant things that would form a skyline \
(towers, monuments, distant structures).
    "scattered"  -- everything else: casual clutter distributed in groups.

Rules:
- At most TWO objects may be "landmark". Use it sparingly, only for genuine \
centrepieces the description calls out; if nothing stands out that way, give \
no object that placement.
- Only static decoration: statues, structures, furniture, signage, vehicles- \
as-scenery, fountains, etc. Never people, animals' faces, text, logos, \
hazards, items, or gameplay mechanics.
- Only extract something if the description actually names a distinctive \
object. If it only describes mood, terrain, weather, or color, return an \
empty key_assets list rather than inventing filler."""


def build_user_prompt(description: str) -> str:
    return f"Player description of the world they want: {description!r}\nExtract the key assets, if any."
