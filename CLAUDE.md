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
  VLM (Claude vision)      → scene info + mood + object bounding boxes
  ObjectCropper            → one image crop per object
  Text-asset extraction    → key props named in the description (Claude)
  Filler-asset suggestion  → generic search keywords to diversify (Claude)
  Meshy Image-to-3D        → one async mesh task per crop
  Meshy Text-to-3D         → one async mesh task per extracted text asset
  Poly Pizza search        → one library lookup per filler keyword
        ↓
WorldRecipe (versioned JSON contract — schemas/world_recipe.schema.json)
  objects[].type = VLM label or text-extracted label, objects[].placement =
  VLM hint (landmark | roadside | background | scattered), objects[].asset
  = {task_id} (optional)
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
✓ One simple loop track, with seed-derived hills and one drop scaled by
  `track.difficulty` (road on embankments; objects rest on the local
  ground height — `GeneratedTrack.GroundHeightAt`)
✓ Two local players, same keyboard
✓ Split-screen (top/bottom)
✓ Keyboard controls (no controller support yet)
✓ Basic laps/checkpoints/winner determination, with a per-player HUD (lap
  counter + toon speed bar, green→red as it fills, gold on boost —
  `UI/RaceHUD.cs`)
✓ Deterministic generation from a seed
✓ Offline fallback (DefaultWorldRecipe) if the backend call fails
✓ VLM object extraction (Claude vision) + Meshy image-to-3D meshes for the
  objects in the photo, behind interfaces; mock providers are the default
✓ Text-based key-asset extraction (Claude, text-only) for props named in the
  description but not necessarily in the photo, + Meshy text-to-3D meshes
  for them — see docs/decisions/0007-text-to-3d-key-assets.md
✓ Generic filler-asset suggestion (Claude, photo+text) + retrieval from a
  public asset library (Poly Pizza) to diversify and fill out the world
  cheaply, for scattered/roadside/background objects only — see
  docs/decisions/0009-generic-filler-from-poly-pizza.md. Which search result
  (if any) to use is picked by a `MatchPickerService`: a per-keyword Claude
  semantic pick by default, or free word-overlap scoring
  (`MATCH_PICKER_PROVIDER=heuristic`) — see
  docs/decisions/0011-claude-match-picker.md
✓ "Generate personalized assets" toggle on the upload screen, **off by
  default**: when off, Meshy is skipped entirely and the whole world
  (including up to 2 landmarks) is sourced from library retrieval instead,
  with filler prompted to translate what's actually in the photo/text into
  generic search terms before rounding out with unrelated filler — see
  docs/decisions/0012-personalize-toggle.md. When personalize=True, the
  photo/text/filler extraction results (which never see each other's
  suggestions) are reconciled by one more Claude call
  (`AssetMergeService`) before anything is submitted to Meshy/the library,
  picking the actual final composition (at most 2 landmarks, everything
  else roadside/scattered) instead of the naive first-come-first-served
  demotion — mock by default (`ASSET_MERGE_PROVIDER`). The single
  `"background"` slot is hardcoded to always be filler's own candidate
  (guaranteed to exist) rather than left to the merge call, so it's never
  at the mercy of whether the photo/text sources happened to detect a
  skyline — see docs/decisions/0014-asset-merge-final-composition.md
✓ Async mesh delivery: the world is built on primitive placeholders and
  generated meshes swap in when ready (placeholders stay if generation
  fails). By default the Generating screen waits for the meshes, with a
  timeout (`GameConfig.waitForGeneratedMeshes` / `meshWaitTimeoutSeconds`);
  turn it off to race immediately while they stream in. When the player's
  "Generate personalized assets" toggle was on, this wait is not optional:
  the screen always waits, with no timeout, regardless of those two
  settings, since racing on unswapped placeholders would defeat the point
  of asking for a personalized world
✓ Pass-through track obstacles (boost, 0.5s paralyze, short spin),
  reshuffled on the racing line each world generation, drawn as runtime-built
  signs (green `>>`, a STOP sign, Subway-style opposing arrows —
  `World/ObstacleVisuals.cs`), each also pulsing a colour-coded ground-ring
  beacon (green/red/yellow) so it reads from further away than the sign
  shape alone (`World/TrackObstacle.cs`'s `BuildBeacon`)
✓ Cel-shaded look on everything (placeholders, track, karts, generated
  meshes), with the shadow band and rim light driven by the recipe palette
  (`GameConfig.toonShading`, `Assets/Scripts/Rendering/ToonStyle.cs` — see
  docs/decisions/0008-toon-shading.md); matching cartoon particle effects
  (outlined hard-edged shapes, stepped colours, shrink-out) in
  `Players/KartParticles.cs`
✓ Comic SFX per kart (putt-putt engine loop revved by the accelerator key, skid "skrrt", hit clang,
  power-up/down stings, a lap chime that climbs in pitch each lap) from CC0
  Freesound clips in `Resources/Audio/` (`Players/KartAudio.cs`;
  `tools/sfx/fetch_sfx.py` fetches + trims them, `Resources/Audio/CREDITS.txt`
  lists sources), a win fanfare + crowd cheer with the finish stripe and a
  chime for the runner-up (`UI/ResultsUI.cs`), plus UI hover/press sounds
  on every button and toggle (`UI/UISounds.cs`, Kenney CC0), and a big
  animated comic countdown (popping numerals over a starburst, beeps and an
  air horn on GO! — `UI/GenerationUI.cs`)
✓ Mood-matched soundtrack: the VLM picks `world.mood` (cheerful | chill |
  epic | spooky | energetic) from the photo + description, and Unity's
  `Audio/MusicPlayer.cs` loops the matching CC0 track from
  `Resources/Audio/Music/` from the countdown through results — see
  docs/decisions/0010-mood-soundtracks.md. No world ambience or dynamic music

✗ Two separate player prompts / per-player world inputs
✗ Network / online multiplayer, matchmaking
✗ Items, weapons, hazards, powerups (beyond the pass-through track obstacles)
✗ Multiple track templates, branching tracks, jumps
✗ Complex procedural terrain
✗ Asset retrieval / asset-pack lookup for *specific* objects (photo/text
  objects are still always generated, never looked up) — retrieval is used
  only for generic filler, see docs/decisions/0009-generic-filler-from-poly-pizza.md
✗ Real LLM-based world synthesis (theme/palette/track are still a
  deterministic mock; only vision, text-asset extraction, and mesh
  generation are real vendors)
✗ Ground texture / skybox generation from text (deferred — see
  docs/decisions/0007-text-to-3d-key-assets.md)
✗ Persisting generated meshes across backend restarts
✓ Comic-book Boot + Upload screens: halftone paper, inked panel frames, a
  speech-bubble controls hint, onomatopoeia word bursts, a slam-in entrance
  and hover wobble on every control -- all rasterised at runtime by
  decorating the scene-baked uGUI hierarchy in `Awake` (`UI/ComicStyle.cs`,
  see docs/decisions/0013-comic-lobby-ui.md). Generating / HUD / Results
  keep their existing look

✗ Final VFX, production auth, cloud deployment
✗ Webcam capture or drag-and-drop image upload (file-picker only, Editor-only for now — see docs/decisions/0003-image-picker-stub.md)
```

## Module ownership

| Owner | Directories |
|---|---|
| Person A — AI / backend / WorldRecipe | `backend/**` (vision, text-asset extraction, filler-asset suggestion, cropper, Meshy client, Poly Pizza client, `/generate-world`, `/assets`), `schemas/**`, `unity/Assets/Scripts/AI/**` (incl. `MeshAssetClient.cs`) |
| Person B — Unity gameplay / track / racing | `unity/Assets/Scripts/Core/**`, `Players/**`, `Racing/**`, `World/TrackGenerator.cs`, `World/WorldGenerator.cs` |
| Person C — assets / generated meshes / environment | `unity/Assets/Scripts/Assets/**` (namespace `MarioKart.AssetsSystem`, incl. `GeneratedMeshLoader.cs`), `World/EnvironmentGenerator.cs` |
| Person D — UI / upload flow / QA | `unity/Assets/Scripts/UI/**`, `Input/**`, manual playtesting |

Changes to `schemas/world_recipe.schema.json` affect every module and need
sign-off from whoever owns the consuming code on both sides (Persons A and B
at minimum) — see `docs/development.md`'s conventions section.

## Architecture rules

1. **AI generates structured data (and meshes). Unity generates the game.**
   The AI backend never touches a Unity object; Unity never calls an AI
   model, the mesh provider, or the asset library directly — every GLB,
   generated or retrieved, is proxied through the backend.
2. **`WorldRecipe` is the backend/frontend contract.** It (plus the mesh
   bytes its `objects[].asset.task_id` handles point at) is the only thing
   that crosses that boundary — see `schemas/world_recipe.schema.json` and
   `docs/world-recipe.md`. Both `backend/app/models/world_recipe.py` and
   `unity/Assets/Scripts/AI/WorldRecipe.cs` must stay in sync with it.
3. **Generation is deterministic from the recipe's seed.** Every procedural
   system derives its randomness via `WorldRandom.DeriveSeed`, never
   `UnityEngine.Random`'s global state — see `docs/decisions/0004-seed-derivation.md`.
4. **Unity only talks to the backend API** — `POST /generate-world`, then
   `GET /assets/{task_id}` / `GET /assets/{task_id}/model.glb` for every
   mesh, generated or retrieved (see
   `docs/decisions/0006-meshy-async-mesh-generation.md` and
   `docs/decisions/0009-generic-filler-from-poly-pizza.md`). No other
   networking: this is a local, same-keyboard game.
5. **Do not add gameplay features outside the current scope** (items,
   obstacles, multiple tracks, etc.) without team agreement — update this
   file's scope checklist when scope changes.
6. **The game must survive AI failure.** Any backend error or timeout falls
   back to `DefaultWorldRecipe` in Unity so the game stays fully playable
   offline. The race can never be *blocked* by mesh generation: the world
   is built on primitive placeholders, and if a mesh fails, times out, or
   the provider is mocked, the placeholder simply stays. Waiting for meshes
   on the Generating screen is allowed only behind a timeout
   (`meshWaitTimeoutSeconds`) — see the amendment in ADR 0006.

## Repository structure

See `README.md` for the full tree. In short: `schemas/` is the contract,
`backend/` implements the AI side of it, `unity/` implements the game side.
`docs/` holds architecture notes, the WorldRecipe reference, the dev
workflow (including a required one-time manual Unity Editor setup step —
see `docs/development.md`), and ADRs under `docs/decisions/`.

## Known limitations of this bootstrap

- Unity scenes and `.meta` files are never hand-authored — they need the
  Editor's GUID allocation. `Assets/Scenes/Main.unity` is (re)built by the
  **MarioKart → Build Main Scene** menu item (`Assets/Editor/MainSceneBuilder.cs`),
  which is the source of truth for the hierarchy and Inspector wiring. See
  `docs/decisions/0005-no-handauthored-unity-assets.md` and
  `docs/development.md`.
- Unity-side determinism (`WorldRandom`, `TrackGenerator`) compiles but has
  not been run end-to-end — verify inside the Editor before relying on it.
- Real image picking (native file dialog) only works inside the Unity
  Editor; standalone builds use a bundled placeholder image.
- The mesh task registry is in-memory: restarting the backend orphans any
  in-flight `task_id`s (Unity gets 404s and keeps placeholders).
- The toon shaders (`Assets/Resources/Shaders/`) and `ToonStyle` have not
  been compiled or run in the Editor yet; if a shader fails to compile,
  everything falls back to `Standard` with one warning.
- `GeneratedMeshLoader` / glTFast import has not been run in the Editor
  either; the Meshy and Claude clients have only been exercised against
  scripted fakes in `backend/tests/`.
