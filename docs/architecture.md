# Architecture

## Core principle

> **AI decides WHAT the world should be. Unity decides HOW to construct it.**

The AI backend never manipulates Unity objects, and Unity never calls an AI
model or the mesh provider directly. What crosses the boundary is a versioned
`WorldRecipe` JSON document (see [`world-recipe.md`](world-recipe.md)) plus,
later, the generated GLB meshes its `objects[].asset` handles point at — both
served by the backend.

## Components

```
┌──────────────────────────┐    POST /generate-world (image + text)    ┌────────────────────────────────┐
│   Unity (client)         │ ────────────────────────────────────────▶ │   Backend (FastAPI)             │
│                          │                                            │                                │
│  UI (ImageUploadUI)      │ ◀──────────────────────────────────────── │  api/generate_world.py         │
│  AI (WorldRecipeClient,  │    { "world_recipe": {... objects[].asset}}│    VisionService (Claude/mock) │
│      MeshAssetClient)    │                                            │    object_cropper              │
│  Core (GameManager)      │    GET /assets/{task_id}   (poll)          │    MeshGenerationService       │──▶ Meshy
│  World (WorldGenerator,  │ ────────────────────────────────────────▶ │      (Meshy/mock)              │    Image-to-3D
│    TrackGenerator,       │ ◀──────────────────────────────────────── │    WorldSynthesisService (mock)│
│    EnvironmentGenerator) │    { "status": "pending|ready|failed" }    │  api/assets.py                 │
│  Assets (AssetResolver,  │                                            │    MeshTaskRegistry            │
│    GeneratedMeshLoader)  │    GET /assets/{task_id}/model.glb         │  models/WorldRecipe            │
│  Players/Racing/Camera   │ ◀──────────────────────────────────────── │                                │
└──────────────────────────┘    model/gltf-binary                       └────────────────────────────────┘
```

- **VisionService** (backend): image bytes + description → `SceneUnderstanding` (dominant colors, brightness, tags, and `detected_objects` with normalized bounding boxes). `MockVisionService` is the default; `ClaudeVisionService` makes one Claude vision call with structured output.
- **object_cropper** (backend): cuts each detected object out of the uploaded image as a PNG crop (padded, clamped, degenerate boxes dropped).
- **MeshGenerationService** (backend): crop → asynchronous mesh task. `MockMeshGenerationService` (default) issues no tasks; `MeshyMeshGenerationService` submits the crop to Meshy Image-to-3D, maps its status, and downloads/caches the GLB.
- **WorldSynthesisService** (backend): scene + description + mesh tasks → `WorldRecipe`. Still a deterministic mock for theme/palette/track; `objects[]` comes from the detected objects, each with its `asset` handle if a task was issued.
- **MeshTaskRegistry** (backend): in-memory `task_id → task` so `/assets` rejects unknown IDs.
- **WorldRecipeClient** (Unity): POSTs the single world input, parses the response into a `WorldRecipe`, or reports an error.
- **MeshAssetClient** (Unity): polls `GET /assets/{task_id}` and downloads `model.glb`. Together with `WorldRecipeClient` this is Unity's entire network surface.
- **GameManager** (Unity): owns the `GameState` machine and orchestrates the flow between UI, the backend call, and world generation.
- **WorldGenerator / TrackGenerator / EnvironmentGenerator** (Unity): turn a `WorldRecipe` into an actual scene — one loop track (road mesh + barrier walls + finish line), checkpoints, and a composed placeholder environment driven by the VLM's per-object `placement` hint (landmarks at key spots, roadside lines, a far horizon layer, zoned clusters of scattered filler, per-instance scale/mirror/tilt/tint variation), all deterministic from `recipe.seed`.
- **AssetResolver** (Unity): maps a recipe object entry to the primitive placeholder to spawn now (tuned for known labels, keyword heuristic for anything the VLM invents) plus the mesh task that will replace it.
- **GeneratedMeshLoader** (Unity): for each mesh task, polls until ready, downloads the GLB, imports it once with glTFast, and places a copy over every placeholder of that type without moving it. Failure/timeout leaves the placeholder.

## One race, sequence

```
Boot ── Start ──▶ Input ── pick image + type description ──▶ Generating
  │                                                               │
  │                                          POST /generate-world │
  │                                                               ▼
  │                                                      Backend: VLM → crops → Meshy submit
  │                                                      returns WorldRecipe at once (or errors)
  │                                                               │
  │                              success ─────────┬────── failure │
  │                                                ▼               ▼
  │                                     WorldGenerator.Generate(recipe)
  │                                     (failure uses DefaultWorldRecipe)
  │                                                │
  │                                                ▼
  │                                          WorldReady ──▶ Countdown ──▶ Racing
  │                                                │                         │
  │                    GeneratedMeshLoader: poll GET /assets/{id} ...        │
  │                    ready → download GLB → swap over placeholders         │
  │                    (failed/timeout → placeholders stay; race unaffected) │
  │                                                                          │
  │                                            both players cross the line ─┘
  │                                                                          ▼
  └───────────────────────────────── Play Again ◀── Results ◀── Finished ◀──┘
```

## Why Unity only talks to the backend

This is a local, same-keyboard 2-player game. There is no online multiplayer,
no matchmaking, and no state synchronization. Unity's whole network surface
is the backend API: the request that fetches the `WorldRecipe`, and the
`/assets` polls/downloads for meshes that request kicked off. Unity never
contacts the vision or mesh vendors — see
`docs/decisions/0006-meshy-async-mesh-generation.md`. Anything resembling
network game state is out of scope — see `CLAUDE.md`'s scope checklist.

## Determinism

Every procedural system derives its randomness from `recipe.seed` via
`WorldRandom.DeriveSeed`, never from `UnityEngine.Random`'s global state, so
the same `WorldRecipe` always produces the same track and environment. See
`docs/decisions/0004-seed-derivation.md`. Generated meshes don't affect
layout: they adopt the placeholders' seed-derived transforms, so a world is
laid out identically whether or not its meshes ever arrive.
