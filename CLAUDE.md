# CLAUDE.md

Guidance for anyone (human or AI) working in this repository.

## Project

A local, same-keyboard 2-player split-screen kart racing game with an
AI-generated environment. A user supplies **one image and one text
description**; a backend turns that into a structured `WorldRecipe`, and
turns the important objects in the photo into 3D meshes (a VLM finds and
crops them, Meshy generates a model from each crop); Unity uses the recipe
to build a themed environment around one simple loop track and swaps the
generated meshes in as they finish. Both players then race on that shared
world.

## Core flow

```
One Image + One Text Description
        ↓
AI backend (FastAPI, mock by default)
  VLM (Claude vision) → scene info + object bounding boxes
  ObjectCropper      → one image crop per object
  Meshy Image-to-3D  → one async mesh task per crop
        ↓
WorldRecipe (versioned JSON contract — schemas/world_recipe.schema.json)
  objects[].type  = VLM label,  objects[].asset = {task_id} (optional)
        ↓
Unity (WorldGenerator → TrackGenerator + EnvironmentGenerator)
        ↓
Generated Loop Track + primitive placeholder environment   ── race starts
        ↓                                                          │
GeneratedMeshLoader polls GET /assets/{task_id}, swaps GLBs in    │
        ↓                                                          ▼
2 Player Split-Screen Race (P1: WASD, P2: Arrow keys)
```

## Current scope

```
✓ One image
✓ One text description
✓ One generated world, shared by both players
✓ One simple loop track
✓ Two local players, same keyboard
✓ Split-screen (top/bottom)
✓ Keyboard controls (no controller support yet)
✓ Basic laps/checkpoints/winner determination
✓ Deterministic generation from a seed
✓ Offline fallback (DefaultWorldRecipe) if the backend call fails
✓ VLM object extraction (Claude vision) + Meshy image-to-3D meshes for the
  objects in the photo, behind interfaces; mock providers are the default
✓ Async mesh delivery: race starts on primitive placeholders, generated
  meshes swap in when ready (placeholders stay if generation fails)

✗ Two separate player prompts / per-player world inputs
✗ Network / online multiplayer, matchmaking
✗ Obstacles, items, weapons, hazards, powerups, boosts
✗ Multiple track templates, branching tracks, jumps
✗ Complex procedural terrain
✗ Asset retrieval / asset-pack lookup (replaced by mesh generation)
✗ Real LLM-based world synthesis (theme/palette/track are still a
  deterministic mock; only vision + mesh generation are real vendors)
✗ Persisting generated meshes across backend restarts
✗ Polished UI, final VFX, production auth, cloud deployment
✗ Webcam capture or drag-and-drop image upload (file-picker only, Editor-only for now — see docs/decisions/0003-image-picker-stub.md)
```

## Module ownership

| Owner | Directories |
|---|---|
| Person A — AI / backend / WorldRecipe | `backend/**` (vision, cropper, Meshy client, `/generate-world`, `/assets`), `schemas/**`, `unity/Assets/Scripts/AI/**` (incl. `MeshAssetClient.cs`) |
| Person B — Unity gameplay / track / racing | `unity/Assets/Scripts/Core/**`, `Players/**`, `Racing/**`, `World/TrackGenerator.cs`, `World/WorldGenerator.cs` |
| Person C — assets / generated meshes / environment | `unity/Assets/Scripts/Assets/**` (namespace `MarioKart.AssetsSystem`, incl. `GeneratedMeshLoader.cs`), `World/EnvironmentGenerator.cs` |
| Person D — UI / upload flow / QA | `unity/Assets/Scripts/UI/**`, `Input/**`, manual playtesting |

Changes to `schemas/world_recipe.schema.json` affect every module and need
sign-off from whoever owns the consuming code on both sides (Persons A and B
at minimum) — see `docs/development.md`'s conventions section.

## Architecture rules

1. **AI generates structured data (and meshes). Unity generates the game.**
   The AI backend never touches a Unity object; Unity never calls an AI
   model or the mesh provider directly — generated GLBs are proxied through
   the backend.
2. **`WorldRecipe` is the backend/frontend contract.** It (plus the mesh
   bytes its `objects[].asset.task_id` handles point at) is the only thing
   that crosses that boundary — see `schemas/world_recipe.schema.json` and
   `docs/world-recipe.md`. Both `backend/app/models/world_recipe.py` and
   `unity/Assets/Scripts/AI/WorldRecipe.cs` must stay in sync with it.
3. **Generation is deterministic from the recipe's seed.** Every procedural
   system derives its randomness via `WorldRandom.DeriveSeed`, never
   `UnityEngine.Random`'s global state — see `docs/decisions/0004-seed-derivation.md`.
4. **Unity only talks to the backend API** — `POST /generate-world`, then
   `GET /assets/{task_id}` / `GET /assets/{task_id}/model.glb` for generated
   meshes (see `docs/decisions/0006-meshy-async-mesh-generation.md`). No
   other networking: this is a local, same-keyboard game.
5. **Do not add gameplay features outside the current scope** (items,
   obstacles, multiple tracks, etc.) without team agreement — update this
   file's scope checklist when scope changes.
6. **The game must survive AI failure.** Any backend error or timeout falls
   back to `DefaultWorldRecipe` in Unity so the game stays fully playable
   offline. The race never waits on mesh generation: it starts on primitive
   placeholders, and if a mesh fails, times out, or the provider is mocked,
   the placeholder simply stays.

## Repository structure

See `README.md` for the full tree. In short: `schemas/` is the contract,
`backend/` implements the AI side of it, `unity/` implements the game side.
`docs/` holds architecture notes, the WorldRecipe reference, the dev
workflow (including a required one-time manual Unity Editor setup step —
see `docs/development.md`), and ADRs under `docs/decisions/`.

## Known limitations of this bootstrap

- No Unity scene, prefab, or `.meta` files are committed — they require the
  Editor's GUID allocation and cannot be safely hand-authored. See
  `docs/decisions/0005-no-handauthored-unity-assets.md` and the setup
  checklist in `docs/development.md`.
- Unity-side determinism (`WorldRandom`, `TrackGenerator`) has not been
  compiled or run — only reviewed for syntax/consistency. Verify inside the
  Editor before relying on it.
- Real image picking (native file dialog) only works inside the Unity
  Editor; standalone builds use a bundled placeholder image.
- The mesh task registry is in-memory: restarting the backend orphans any
  in-flight `task_id`s (Unity gets 404s and keeps placeholders).
- `GeneratedMeshLoader` / glTFast import has not been run in the Editor
  either; the Meshy and Claude clients have only been exercised against
  scripted fakes in `backend/tests/`.
