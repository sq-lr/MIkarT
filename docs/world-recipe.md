# WorldRecipe

The `WorldRecipe` is the only contract that crosses the AI/Unity boundary.
The AI backend produces it; Unity consumes it and builds the actual scene.
Neither side needs to know anything about the other's internals beyond this
JSON shape.

Canonical schema: [`../schemas/world_recipe.schema.json`](../schemas/world_recipe.schema.json).

## Fields

| Field | Type | Notes |
|---|---|---|
| `version` | integer, `1` | Bump on breaking schema changes. Consumers should log a warning (not crash) on an unrecognized version — see `WorldRecipeClient.cs`. |
| `seed` | integer ≥ 0 | Master seed. All procedural systems derive sub-seeds from this via `WorldRandom.DeriveSeed(seed, label)` — see `docs/decisions/0004-seed-derivation.md`. |
| `world.name` | string | Display name, e.g. "Tropical Paradise". |
| `world.theme` | string | Free-text theme label for display/logging. Not used for asset lookups. |
| `world.terrain` | enum: `sand`, `grass`, `snow`, `dirt`, `rock`, `mud` | Drives ground material selection. |
| `world.weather` | enum: `sunny`, `rainy`, `cloudy`, `snowy`, `clear` | Placeholder — not wired to any weather system yet. |
| `world.time_of_day` | enum: `day`, `night`, `dusk`, `dawn` | Placeholder — not wired to any lighting rig yet beyond palette tinting. |
| `track.width` | number, 4–20 | Meters. |
| `track.length` | number, 100–2000 | Approximate loop length in meters; drives the loop radius in `TrackGenerator`. |
| `track.difficulty` | number, 0–1 | Controls how much the loop's curvature is perturbed. |
| `objects[].type` | string | e.g. `palm_tree`, `rock`. Looked up in `AssetResolver`'s registry; unknown types fall back to a generic gray cube. |
| `objects[].density` | number, 0–1 | Placeholder linear density → instance count model in `EnvironmentGenerator`. |
| `palette` | array of hex colors, 1–8 | `palette[0]` tints the sun light, `palette[1]` tints the placeholder ground material. |

The enum vocabularies for `terrain`/`weather`/`time_of_day` were chosen by
this bootstrap and are **not yet confirmed with a real vision/LLM
provider** — see `docs/decisions/0003-image-picker-stub.md` and the note in
`schemas/world_recipe.schema.json`. Extend the enum and `AssetResolver`'s
registry together when adding a new value.

## Example

```json
{
  "version": 1,
  "seed": 482913,
  "world": {
    "name": "Tropical Paradise",
    "theme": "tropical beach",
    "terrain": "sand",
    "weather": "sunny",
    "time_of_day": "day"
  },
  "track": {
    "width": 8.0,
    "length": 800.0,
    "difficulty": 0.5
  },
  "objects": [
    { "type": "palm_tree", "density": 0.5 },
    { "type": "rock", "density": 0.2 }
  ],
  "palette": ["#2E8B57", "#F4D35E", "#2D9CDB"]
}
```

This exact example is the mock backend's default output when the user's
description doesn't match any other theme keyword (see
`backend/app/services/world_synthesis_service.py`).
