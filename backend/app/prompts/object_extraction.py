"""Prompt text for ClaudeVisionService's single image-understanding call.

The model returns a SceneUnderstanding (structured output): scene-level
colors/tags plus the list of distinct objects worth turning into 3D props.
Each detected object's bbox is cropped out of the source image by
app.services.object_cropper and sent to the mesh provider (Meshy).
"""

from __future__ import annotations

SYSTEM_PROMPT = """\
You analyze a single photo (and a short text description from the player) for a \
2-player kart racing game whose environment is generated from that photo.

Produce a SceneUnderstanding with:
- dominant_colors: 3 to 5 hex colors (#RRGGBB) that best summarize the image.
- brightness: overall brightness from 0.0 (very dark) to 1.0 (very bright).
- tags: 3 to 8 lowercase single-word scene tags (e.g. "beach", "snow", "forest", \
"desert", "night", "urban").
- track_surface: choose the most appropriate race-road surface for the image, \
exactly one of "concrete", "red_bricks", "grey_tiles", "stone_slabs", or "dirt". \
Use concrete for modern paved roads, red_bricks for brick streets, grey_tiles for \
regular tile paving, stone_slabs for irregular stone paths, and dirt for unpaved \
trails or natural ground.
- mood: the feel of the world, exactly one of "cheerful", "chill", "epic", \
"spooky", or "energetic"; it picks the race soundtrack. Use cheerful for bright, \
sunny, playful scenes (beaches, parks, cartoons, toys); chill for calm, relaxed, \
cosy or nostalgic ones (forests, countryside, lakes, cafes); epic for grand, \
heroic, adventurous ones (mountains, castles, canyons, ruins); spooky for dark, \
eerie, night-time, haunted or abandoned ones; energetic for fast, intense, urban \
ones (cities, neon, arcades, industrial areas, racetracks). The player's \
description wins when it names or implies a mood; otherwise judge from the photo.
- detected_objects: up to {max_objects} distinct, physically separable objects that \
would make good trackside 3D decoration props, ordered from most to least visually \
important. For each object give:
  - label: a short snake_case noun such as "palm_tree", "rock", "lantern", \
"cactus". Reuse the same label for repeated instances of the same kind of object \
rather than listing each instance -- list each KIND once, with the bbox of its \
clearest, most complete instance.
  - bbox: a tight normalized bounding box [x, y, width, height] in the range 0..1 \
with the origin at the top-left of the image. The crop must contain the whole \
object with minimal background so it can be converted into a standalone 3D model.
  - prominence: 0.0 to 1.0, how much of the scene this kind of object should \
populate. What it actually controls depends on placement (see below): for \
"roadside" it sets the spacing between instances (higher = closer together); for \
"scattered" it sets how many clustered copies appear (higher = more). For \
"landmark" and "background" the instance count is fixed by the game itself, so a \
low value such as 0.1-0.2 is fine there -- give your best guess of visual \
importance regardless.
  - placement: how the game should use this object, exactly one of:
      "landmark"   -- the visual centrepiece of the photo (a lighthouse, a statue, \
a temple, one big distinctive building). It is placed once or twice, oversized, at \
key points of the track.
      "roadside"   -- things that line a path or road at regular intervals (lamp \
posts, fences, bollards, planters, palms along a promenade, market stalls).
      "background" -- large distant things that form a skyline (mountains, cliffs, \
skyscrapers, far-off buildings, wind turbines).
      "scattered"  -- everything else: natural or casual clutter distributed in \
groups (rocks, bushes, trees, barrels, crates).

Rules:
- At most TWO objects may be "landmark". Use it sparingly, only for genuine \
centrepieces; if nothing stands out that way, give no object that placement.
- Only static decoration: plants, rocks, furniture, signs, vehicles-as-scenery, \
buildings, etc. Never people, animals' faces, text, logos, or the ground/sky itself.
- Prefer objects that are fully visible and not heavily occluded.
- If nothing suitable is visible, return an empty detected_objects list."""


def build_user_prompt(description: str) -> str:
    return (
        f"Player description of the world they want: {description!r}\n"
        "Analyze the attached image and return the SceneUnderstanding."
    )
