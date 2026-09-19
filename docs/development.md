# Development Workflow

## Backend

```bash
cd backend
python3 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
cp .env.example .env

pytest                                    # run tests
uvicorn app.main:app --reload --port 8000 # run the mock server
```

No API keys are required — `AI_PROVIDER=mock` / `MESH_PROVIDER=mock` in
`.env.example` use the built-in `MockVisionService` / `MockMeshGenerationService`.
For the real pipeline set `AI_PROVIDER=claude` + `ANTHROPIC_API_KEY` and
`MESH_PROVIDER=meshy` + `MESHY_API_KEY` in `backend/.env` (see
`backend/README.md`). Selecting a real provider without its key fails at the
first request with a clear error.

Tests never touch the network: the Meshy client is tested through
`httpx.MockTransport`, the Claude client through a stub, and the `/assets`
routes through `app.services.providers.override(...)`.

## Unity — one-time Editor setup checklist

This repository ships every `.cs` script and the folder structure, but it
**does not** include a Unity scene, prefabs, or `.meta` files — those require
Unity's Editor asset database to allocate GUIDs correctly, and hand-inventing
them risks silently corrupting the project (see
`docs/decisions/0005-no-handauthored-unity-assets.md`). Whoever opens the
project first must run this checklist once, then commit the results:

1. Install **Unity Hub** and **Unity 2022.3.x LTS** (pinned in
   `unity/ProjectSettings/ProjectVersion.txt`; any 2022.3.x patch will open
   the project, an exact match isn't required).
2. Open `unity/` as a project in Unity Hub. Let it import — this generates
   `Library/`, resolves `Packages/manifest.json` (including
   `com.unity.nuget.newtonsoft-json` and `com.unity.cloud.gltfast`, which
   pulls in Burst/Collections/Mathematics), and creates `.meta` files for
   every script.
3. Create `Assets/Scenes/Main.unity` and set it as the only scene in Build
   Settings. This is the one and only scene for the whole game — `GameState`
   drives which UI panel is visible, not scene loading.
4. Create empty GameObjects and attach scripts:
   - `GameManager` → `Core/GameManager.cs`
   - `WorldRecipeClient` (can be the same or a child GameObject) → `AI/WorldRecipeClient.cs`
   - `MeshAssetClient` (same GameObject as `WorldRecipeClient` is fine) → `AI/MeshAssetClient.cs`
   - `WorldGenerator` → `World/WorldGenerator.cs`, with a child `EnvironmentGenerator` → `World/EnvironmentGenerator.cs`
     **and**, on that same `EnvironmentGenerator` GameObject, `Assets/GeneratedMeshLoader.cs`
     (not on a child — `EnvironmentGenerator.Generate` destroys its children)
   - `RaceManager` → `Racing/RaceManager.cs`
   - Two kart GameObjects, each with a `Rigidbody` + `Players/KartController.cs` + `Players/PlayerController.cs` + `Input/PlayerInput.cs` (set `playerIndex` to 1/2)
   - Two `Camera` GameObjects, each with `Camera/PlayerCamera.cs`; call `SetViewportForPlayer(1)` / `SetViewportForPlayer(2)` (e.g. from a small bootstrap script or the Inspector)
   - A `Canvas` with five child panels, each with its matching `UI/*.cs` script (`LobbyUI`, `ImageUploadUI`, `GenerationUI`, `RaceHUD`, `ResultsUI`)
5. Wire the public/`[SerializeField]` references between these objects in the
   Inspector (e.g. `GameManager.recipeClient`, `GameManager.meshAssetClient`,
   `GameManager.worldGenerator`, `GameManager.raceManager`,
   `GameManager.resultsUI`, `GeneratedMeshLoader.client` → the
   `MeshAssetClient`, each `PlayerController`'s `input`/`kart`, each
   `RaceHUD`'s `player1Laps`/`player2Laps`). This is the step that
   substitutes for pre-wired GUIDs.
6. Add `Collider(isTrigger = true)` + `LapManager` components under each kart
   so `Checkpoint.OnTriggerEnter` can find them via `GetComponentInParent`.
7. Save the scene and commit the generated `.unity`, `.meta`, and
   `Library/`-adjacent files that git is supposed to track (see
   `.gitignore` — `Library/` itself is never committed).

Nothing in this repository has been compiled or run inside the Unity Editor
by this bootstrap — the scripts have been written and reviewed for syntax and
consistency, but the above checklist is unverified until a human runs it.

## Manual determinism check (until an Editor is available)

`World/WorldRandom.cs`'s determinism can't be exercised without the Editor.
Once the scene exists: generate a world from the same `WorldRecipe` twice (or
add a debug button that calls `WorldGenerator.Generate` with a fixed test
recipe) and confirm the environment objects land in the same positions both
times — with and without generated meshes loaded (mesh copies adopt the
placeholders' transforms and must never move them).

## Manual generated-mesh check

With `MESH_PROVIDER=meshy` (and `AI_PROVIDER=claude`) in `backend/.env`:

1. Press Play, pick an image, generate. The world must appear immediately
   with primitive placeholders and the countdown must start — the race never
   waits on meshes.
2. Within a few minutes each placeholder type whose recipe entry had an
   `asset` should be replaced by its Meshy mesh at the same position
   (placeholder renderer disabled, `<type>_Placeholder_Mesh` sibling added).
3. Stop the backend mid-poll: after `assetPollTimeoutSeconds` the loader
   logs one warning per type and the placeholders stay. The race is unaffected.
4. `GameConfig.enableGeneratedMeshes = false` skips polling entirely.

## Conventions

- Don't touch `schemas/world_recipe.schema.json` without updating both
  `backend/app/models/world_recipe.py` and
  `unity/Assets/Scripts/AI/WorldRecipe.cs` in the same change, plus
  `docs/world-recipe.md`.
- All Unity randomness goes through `WorldRandom`, never `UnityEngine.Random`.
- `objects[].type` is an open vocabulary (whatever the VLM labels). Adding a
  tuned placeholder to `AssetResolver.KnownTypes` for a frequent label is
  optional polish, not a schema change.
- Every backend provider call goes behind `VisionService` /
  `MeshGenerationService` and is selected in `app/services/providers.py`;
  never call a vendor SDK from a route.
- Unity's only network endpoints are `POST /generate-world` and
  `GET /assets/...` (`WorldRecipeClient`, `MeshAssetClient`). Don't add others.
