# WorldRecipe

The `WorldRecipe` is the contract that crosses the AI/Unity boundary. The AI
backend produces it; Unity consumes it and builds the actual scene. Neither
side needs to know anything about the other's internals beyond this JSON
shape — plus the `/assets/{task_id}` endpoints that its optional
`objects[].asset` handles refer to.

Canonical schema: [`../schemas/world_recipe.schema.json`](../schemas/world_recipe.schema.json).

## Fields

| Field | Type | Notes |
|---|---|---|
| `version` | integer, `1` | Bump on breaking schema changes. Consumers should log a warning (not crash) on an unrecognized version — see `WorldRecipeClient.cs`. |
| `seed` | integer, 0 – 2 147 483 647 | Master seed (must fit a signed 32-bit int — Unity's `WorldRecipe.seed` is an `int`). All procedural systems derive sub-seeds from this via `WorldRandom.DeriveSeed(seed, label)` — see `docs/decisions/0004-seed-derivation.md`. |
| `world.name` | string | Display name, e.g. "Tropical Paradise". |
| `world.theme` | string | Free-text theme label for display/logging. Not used for asset lookups. |
| `world.terrain` | enum: `sand`, `grass`, `snow`, `dirt`, `rock`, `mud` | Drives ground material selection. |
| `world.weather` | enum: `sunny`, `rainy`, `cloudy`, `snowy`, `clear` | Placeholder — not wired to any weather system yet. |
| `world.time_of_day` | enum: `day`, `night`, `dusk`, `dawn` | Placeholder — not wired to any lighting rig yet beyond palette tinting. |
| `world.sky` | enum: `sunny`, `cloudy`, `sunset`, `night`, `indoor` | Selected on the image-upload screen and applied to Unity camera backgrounds and ambient light. `indoor` replaces the sky and clouds with a room built around the track (walls, ceiling, light panels — `WorldGenerator.BuildIndoorRoom`) under flat indoor lighting. Defaults to `sunny` for older recipes. |
| `track.width` | number, 4–20 | Meters. |
| `track.length` | number, 100–2000 | Approximate loop length in meters; drives the loop radius in `TrackGenerator`. |
| `track.difficulty` | number, 0–1 | Controls how much the loop's curvature is perturbed, and how much it climbs: `TrackGenerator` rolls the loop over three broad hills (±30 m) and one short, steeper drop (up to 12 m), both scaled by difficulty and capped at a 50% grade. 0 is a flat track. The road sits on embankments, and every environment object rests on the local road/embankment/plain height (`GeneratedTrack.GroundHeightAt`), so elevation never changes an object's x/z or its per-instance variation. |
| `world.mood` | enum: `cheerful`, `chill`, `epic`, `spooky`, `energetic`; optional, default `energetic` | The feel of the world as judged by the vision model from the photo and the player's description (the description wins when it names or implies a mood). Unity plays the matching CC0 loop from `Resources/Audio/Music/` — see `docs/decisions/0010-mood-soundtracks.md`. Added backwards-compatibly: `version` stays 1. |
| `track.surface` | enum: `concrete`, `red_bricks`, `grey_tiles`, `stone_slabs`, `dirt` | Selected by image analysis from the uploaded scene and rendered with the forest-reference palette: deep teal concrete, terracotta bricks, blue-green grey tiles, mossy stone slabs, or dark earth dirt. Defaults to `concrete` for older recipes. |
| `objects[].type` | string | The snake_case label an object was given, either by the vision model (an object it found in the photo, e.g. `palm_tree`, `lantern`), the text-asset extractor (a prop the player's description named that may not be in the photo, e.g. `whale_statue`), or the filler-asset suggester (a generic prop found in a public library, e.g. `traffic_cone`, see `docs/decisions/0009-generic-filler-from-poly-pizza.md`) — indistinguishable in this field. Open vocabulary. `AssetResolver` has tuned placeholders for common labels and a keyword heuristic for the rest. |
| `objects[].density` | number, 0–1 | From the VLM's `prominence` for a photo-detected object, or the text/filler extractor's own `density` otherwise. For `scattered` types `EnvironmentGenerator` turns it into an instance count (`density × 1.5 × control points`) placed in clusters within a subset of track zones; for `roadside` types it sets the spacing (40 m at 0 → 12 m at 1). The backend lists objects most-prominent first for readability only — order carries no meaning. |
| `objects[].placement` | enum: `landmark`, `roadside`, `background`, `scattered`; optional, default `scattered` | The extractor's judgement of how an object should be used — from the vision model for a photo-detected object, the text-asset extractor for a text-derived one (see `docs/decisions/0007-text-to-3d-key-assets.md`), or the filler-asset suggester for a library-retrieved one (restricted to `roadside`/`background`/`scattered` only — filler is never a `landmark`, see `docs/decisions/0009-generic-filler-from-poly-pizza.md`). `landmark` (at most two per recipe, combined across photo and text sources — the backend demotes extras): the centrepiece, placed once or twice, oversized, opposite the finish line and at the sharpest bend, facing the track; no small copies. `roadside`: lines the road at regular intervals on both sides, facing it (lamp posts, fences, palms on a promenade). `background`: a continuous wall lining both sides of the *entire* track, close in (12–16 m out) and deliberately overlapping itself, so it reads as terrain the road cuts through rather than a distant skyline — a filler keyword tagged `background` also gets a second, distinct library match so the wall mixes two shapes instead of repeating one (see `docs/decisions/0011-claude-match-picker.md`). `scattered`: zoned clusters of varied copies (rocks, bushes, trees), plus a few small sky-tinted copies out at the true horizon. Added backwards-compatibly: `version` stays 1, and Unity treats a missing value as `scattered`. |
| `objects[].asset` | object or absent | `{ "task_id", "provider": "meshy" \| "polypizza" }` — handle to the mesh being generated (Meshy, from an image crop or a text-extracted prompt — see `docs/decisions/0007-text-to-3d-key-assets.md`) or retrieved (Poly Pizza, from a filler keyword search — see `docs/decisions/0009-generic-filler-from-poly-pizza.md`); look identical here regardless of source or provider (see `objects[].source` to tell them apart). Unity polls `GET /assets/{task_id}` and swaps the GLB in when `ready` (a Poly Pizza task reports `ready` immediately, since retrieval already happened by submit time). Absent when no mesh was requested/found (mock provider, offline fallback, crop too small, no library match) or the submit failed; Unity then keeps the placeholder. |
| `objects[].source` | enum: `photo`, `text`, `filler`; optional | Which extraction call produced this object — the vision model (photo), the text-asset extractor (text), or the filler-asset suggester/library retrieval (filler). Debug/logging only (see `EnvironmentGenerator`'s per-object `Debug.Log`); Unity never branches on it for placement or gameplay. Absent for the canned theme-profile fallback objects the mock synthesis uses when nothing was detected, and for `DefaultWorldRecipe`. Added backwards-compatibly: `version` stays 1. |
| `palette` | array of hex colors, 1–8 | `palette[0]` tints the sun/rim light. `palette[1]` tints the ground plane/terrain material and blends into the road/curb color — this is where the filler extraction's theme-matched ground color lands when Claude suggests one (see `docs/decisions/0009-generic-filler-from-poly-pizza.md`); a mock/failed extraction leaves the theme profile's own value untouched. `palette[2]` is an accent color: the source for the toon shadow-band tint, the barrier wall tint, and the sky-haze blend on the distant scattered-horizon flourish. |

The enum vocabularies for `terrain`/`weather`/`time_of_day` were chosen by
this bootstrap; the mock synthesis service still picks them from a keyword
profile rather than from the vision model. `objects[].type` is deliberately
*not* an enum — it's whatever the VLM extracted — so adding a tuned
placeholder to `AssetResolver.KnownTypes` is optional polish, not a contract
change.

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
    "width": 16.0,
    "length": 800.0,
    "difficulty": 0.5
  },
  "objects": [
    { "type": "palm_tree", "density": 0.5, "placement": "roadside", "asset": { "task_id": "0193a0c1-7f1e-7c3a-9b1d-2f0e4a6c8d10", "provider": "meshy" }, "source": "photo" },
    { "type": "rock", "density": 0.2, "placement": "scattered" }
  ],
  "palette": ["#2E8B57", "#F4D35E", "#2D9CDB"]
}
```

With the default mock providers the backend produces this recipe for a
"tropical" description, minus the `asset` handle (the mock mesh provider
issues no tasks — see `backend/app/services/world_synthesis_service.py` and
`mesh_generation_service.py`). With `MESH_PROVIDER=meshy`, each object whose
crop was submitted carries its task ID as shown for `palm_tree`.

## Asset status (`GET /assets/{task_id}`)

```json
{ "status": "pending", "progress": 40 }
{ "status": "ready",   "progress": 100 }
{ "status": "failed",  "progress": 0, "error": "input image rejected" }
```

`GET /assets/{task_id}/model.glb` returns `model/gltf-binary` once ready,
`409` while pending, `404` for an unknown task.
