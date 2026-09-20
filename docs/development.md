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

1. Install **Unity Hub** and **Unity 6000.0.x** (pinned to 6000.0.23f1 in
   `unity/ProjectSettings/ProjectVersion.txt`; any 6000.0.x patch will open
   the project, an exact match isn't required).
2. Open `unity/` as a project in Unity Hub. Let it import — this generates
   `Library/`, resolves `Packages/manifest.json` (including
   `com.unity.nuget.newtonsoft-json` and `com.unity.cloud.gltfast`, which
   pulls in Burst/Collections/Mathematics), and creates `.meta` files for
   every script.
3. Run **MarioKart → Build Main Scene** from the Editor menu bar. This is
   `Assets/Editor/MainSceneBuilder.cs`: it rebuilds `Assets/Scenes/Main.unity`
   from scratch (confirming first if the scene isn't empty), creates the two
   kart materials + ground material under `Assets/Materials/`, wires every
   `[SerializeField]` reference, and registers the scene as the only one in
   Build Settings. Re-run it any time the scene gets into a bad state.
   `Main.unity` is the one and only scene for the whole game — `GameState`
   drives which UI panel is visible, not scene loading.
   If you only need the kart/ground materials re-pointed at the toon shaders
   (`Assets/Resources/Shaders/`, see ADR 0008) — e.g. after pulling a shader
   change — run **MarioKart → Apply Toon Materials** instead; it touches the
   three `.mat` files and leaves the scene alone.
4. Commit the generated `.unity`, `.mat`, and `.meta` files (see
   `.gitignore` — `Library/` itself is never committed).

### What the builder produces

Reference for what's in the scene (and what to recreate by hand if you'd
rather not use the menu item). Field names are the actual `[SerializeField]`
names, so the builder logs an error naming the field if a script renames one.

| GameObject | Components | Wired references |
|---|---|---|
| `GameManager` | `Core/GameManager` (runs first via `DefaultExecutionOrder(-100)`) + `Audio/MusicPlayer` (loops the CC0 track for `recipe.world.mood` from `Resources/Audio/Music/` on its own 2D `AudioSource`, WorldReady → Results; finds `WorldGenerator` itself if unwired) | `recipeClient`, `meshAssetClient`, `worldGenerator`, `raceManager`, `resultsUI`; `MusicPlayer.worldGenerator` |
| ↳ `Backend` | `AI/WorldRecipeClient`, `AI/MeshAssetClient` | — |
| `WorldGenerator` | `World/WorldGenerator` | `environmentGenerator`, `trackVisualRoot`, `checkpointRoot`, `obstacleRoot`, `sunLight`, `groundRenderer` |
| ↳ `EnvironmentGenerator` | `World/EnvironmentGenerator` **and** `Assets/GeneratedMeshLoader` on the same GameObject (not a child — `Generate()` destroys children) | `meshLoader` → self; `GeneratedMeshLoader.client` → `MeshAssetClient` |
| ↳ `TrackVisual` | empty; `TrackMeshBuilder` fills it at runtime with the road ribbon (mesh collider — it's what the karts drive on, since the road has hills), two `TrackBarrier` walls (mesh colliders), and a `Terrain` height-field mesh (sampled from `GeneratedTrack.GroundHeightAt`, sharing the ground plane's material) that forms the embankments under a raised road | — |
| ↳ `Checkpoints` | empty; `WorldGenerator` fills it at runtime with trigger gates facing the track tangent | — |
| ↳ `Obstacles` | `World/ObstacleGenerator`; filled at runtime with pass-through boost / paralyze / spin pickups (green `>>` chevrons / stop sign / opposing-arrow signs, built in `World/ObstacleVisuals.cs`). Collecting one respawns another elsewhere | created at runtime if missing |
| ↳ `Ground` | Plane ×60 (600 m) at y = −0.05 | palette-tinted via `groundRenderer` |
| `Directional Light` | `Light` | palette-tinted via `sunLight` |
| `RaceManager` | `Racing/RaceManager` | `players` → both `LapManager`s |
| `RaceBootstrap` | `Racing/RaceBootstrap` — the glue: on `WorldGenerator.OnWorldReady` (track built, before the mesh wait) parks the karts on the start line and snaps the cameras behind them so the dimmed Generating screen shows the start line, on the `WorldReady` state re-parks them and starts the countdown, on `Racing` unfreezes them, sets split-screen viewports | `worldGenerator`, `raceManager`, `karts[2]`, `cameras[2]` |
| `Kart_P1` / `Kart_P2` | Cube (physics body, renderer hidden at runtime) + `Rigidbody` + `Players/KartController` + `Players/PlayerController` + `Input/PlayerInput` (WASD / arrows) + `Racing/LapManager` + `Players/KartVisual` (go-kart body, driver and spinning/steering wheels built from primitive meshes in code, taking the player colour from the cube's material) + `Players/KartSpeedEffect` (top-speed exhaust flames) + `Players/KartSkidEffect` (smoke, sparks, skid marks on contact) + `Players/KartPickupEffect` (gold ring + star burst + sparkle trail on boost, red ring + X marks on stop, blue ring + dizzy-star halo on spin; all built in code) + `Players/KartAudio` (one comic putt-putt engine loop, pitched up while the accelerator key is held, skid "skrrt", hit clang, pickup stings — 2D `AudioSource`s built in code from the CC0 clips in `Resources/Audio/`, regenerated by `tools/sfx/fetch_sfx.py`; `pitchOffset` ∓0.04 per player) | `rb`, `input`, `kart`, `playerIndex`, `pitchOffset` |
| `Camera_P1` / `Camera_P2` | `Camera` (top / bottom half) + `Camera/PlayerCamera` (chase follow + screen shake on wall and kart-vs-kart hits, via `KartController.Impact` + a slight FOV zoom-out near that kart's top speed, `speedZoomDegrees`) + `Rendering/PaperGrainEffect` + `Rendering/SpeedLinesEffect` (comic dash marks at the edges of that player's half near top speed; builds its own overlay canvas at runtime); `AudioListener` on P1 only | `cam`, `target` |
| `Canvas` | Screen-space overlay, 1920×1080 scaler | — |
| ↳ `LobbyUI` … `ResultsUI` | one always-active holder per `UI/*.cs` script (`LobbyUI`, `ImageUploadUI`, `GenerationUI`, `RaceHUD`, `ResultsUI`), each with a `Panel` child that the script shows/hides. The script must **not** sit on the panel itself: it unsubscribes from `GameManager` in `OnDisable`, so hiding its own GameObject would deafen it permanently. Each script calls `UI/UISounds.Attach` on its buttons/toggles in `Awake` for the hover tick + press chime (Kenney CC0 clips in `Resources/Audio/`). `GenerationUI` also builds a starburst Image behind its status text at runtime for the animated countdown (numerals pop in with beeps, GO! with an air horn; it keeps its panel up for `goHold` seconds into Racing). `LobbyUI` and `ImageUploadUI` go further and **restyle the baked hierarchy in `Awake`** as comic pages via `UI/ComicStyle.cs` (halftone paper, inked frames, speech bubble, word bursts, slam-in entrance) — so the baked child names (`Title`, `Hint`, `StartButton`, `Preview`, `DescriptionField`, `SurfaceHint`, `SkyOptions`, `PersonalizeToggle`/`Checkmark`, `GenerateButton`, and each control's `Label`) are a contract between `MainSceneBuilder` and those scripts; a missing one is skipped with a warning. New comic UI should go through `ComicStyle` rather than raw `Image` colours (see docs/decisions/0013-comic-lobby-ui.md) | `panel` → the child, buttons, texts, `RaceHUD.player1Laps/player2Laps` |
| `EventSystem` | `EventSystem` + `StandaloneInputModule` (old Input Manager) | — |

The UI scripts use legacy `UnityEngine.UI.Text` / `InputField`, so if you
add UI by hand use GameObject → UI → **Legacy**, not the TextMeshPro
variants.

## Manual determinism check (until an Editor is available)

`World/WorldRandom.cs`'s determinism can't be exercised without the Editor.
Once the scene exists: generate a world from the same `WorldRecipe` twice (or
add a debug button that calls `WorldGenerator.Generate` with a fixed test
recipe) and confirm the environment objects land in the same positions both
times — with and without generated meshes loaded (mesh copies adopt the
placeholders' transforms and must never move them).

## Manual generated-mesh check

With `MESH_PROVIDER=meshy` (and `AI_PROVIDER=claude`) in `backend/.env`:

1. Press Play, pick an image, generate. The Generating screen should read
   "Building the track..." then "Waiting for 3D models... 0/2 ready · N%",
   with the placeholder world dimly visible behind it and the percentage
   climbing as Meshy reports progress.
2. Within a few minutes each placeholder type whose recipe entry had an
   `asset` is replaced by its Meshy mesh at the same position (placeholder
   renderer disabled, `<type>_Placeholder_Mesh` sibling added); once all are
   in, the dim clears and the countdown starts.
3. Stop the backend mid-wait: after `meshWaitTimeoutSeconds` (default 300 s)
   the countdown starts anyway on placeholders, with one warning in the
   Console; `assetPollTimeoutSeconds` later the loader gives up per type.
4. `GameConfig.waitForGeneratedMeshes = false` restores stream-in: the
   countdown starts immediately and meshes swap in mid-race.
   `enableGeneratedMeshes = false` skips polling entirely.

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
