"""Prompt text for a future real-LLM WorldSynthesisService.

Unused by MockWorldSynthesisService today. When a real provider is wired in,
its output must still validate against schemas/world_recipe.schema.json --
that contract does not change.
"""

from app.services.vision_service import SceneUnderstanding

SYSTEM_PROMPT = """\
You turn a single photo and a short text description into a WorldRecipe for \
a 2-player kart racing game. You decide WHAT the world should be; you never \
place objects yourself, generate meshes, or write game code -- that is \
Unity's job. Respond with fields matching schemas/world_recipe.schema.json \
exactly: version, seed, world (name, theme, terrain, weather, time_of_day), \
track (width, length, difficulty), objects (type, density) and palette \
(hex colors). Keep objects to decoration only -- no hazards, items, or \
obstacles."""


def build_user_prompt(description: str, scene: SceneUnderstanding) -> str:
    """Build the user-turn prompt for a real LLM call.

    `scene` is the app.services.vision_service.SceneUnderstanding produced by
    VisionService.analyze_image for the uploaded photo.
    """
    return (
        f"User description: {description!r}\n"
        f"Detected scene tags: {scene.tags}\n"
        f"Dominant colors: {scene.dominant_colors}\n"
        f"Brightness: {scene.brightness}\n"
        "Produce a single WorldRecipe JSON object matching the schema."
    )
