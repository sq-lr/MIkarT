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

No API keys are required — `AI_PROVIDER=mock` in `.env.example` uses the
built-in `MockVisionService`/`MockWorldSynthesisService`.

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
   `com.unity.nuget.newtonsoft-json`), and creates `.meta` files for every
   script.
3. Create `Assets/Scenes/Main.unity` and set it as the only scene in Build
   Settings. This is the one and only scene for the whole game — `GameState`
   drives which UI panel is visible, not scene loading.
4. Create empty GameObjects and attach scripts:
   - `GameManager` → `Core/GameManager.cs`
   - `WorldRecipeClient` (can be the same or a child GameObject) → `AI/WorldRecipeClient.cs`
   - `WorldGenerator` → `World/WorldGenerator.cs`, with a child `EnvironmentGenerator` → `World/EnvironmentGenerator.cs`
   - `RaceManager` → `Racing/RaceManager.cs`
   - Two kart GameObjects, each with a `Rigidbody` + `Players/KartController.cs` + `Players/PlayerController.cs` + `Input/PlayerInput.cs` (set `playerIndex` to 1/2)
   - Two `Camera` GameObjects, each with `Camera/PlayerCamera.cs`; call `SetViewportForPlayer(1)` / `SetViewportForPlayer(2)` (e.g. from a small bootstrap script or the Inspector)
   - A `Canvas` with five child panels, each with its matching `UI/*.cs` script (`LobbyUI`, `ImageUploadUI`, `GenerationUI`, `RaceHUD`, `ResultsUI`)
5. Wire the public/`[SerializeField]` references between these objects in the
   Inspector (e.g. `GameManager.recipeClient`, `GameManager.worldGenerator`,
   `GameManager.raceManager`, `GameManager.resultsUI`, each `PlayerController`'s
   `input`/`kart`, each `RaceHUD`'s `player1Laps`/`player2Laps`). This is the
   step that substitutes for pre-wired GUIDs.
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
times.

## Conventions

- Don't touch `schemas/world_recipe.schema.json` without updating both
  `backend/app/models/world_recipe.py` and
  `unity/Assets/Scripts/AI/WorldRecipe.cs` in the same change, plus
  `docs/world-recipe.md`.
- All Unity randomness goes through `WorldRandom`, never `UnityEngine.Random`.
- New object types added to `AssetResolver`'s registry should also be added
  to the `terrain`/`objects[].type` vocabulary discussion in
  `docs/world-recipe.md` if they imply a new terrain/weather value.
