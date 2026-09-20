# MIkarT

A local 2-player split-screen kart racer where one photo and one sentence
generate the world you race in.

## Current features

- `POST /generate-world` FastAPI endpoint that turns an image + description
  (+ a sky preset and a "personalize" flag) into a `WorldRecipe` JSON
  document. Three independent Claude calls run concurrently: a vision call
  finds the important objects in the photo (plus the world's mood and road
  surface), a text-only call extracts key props the description names, and
  a filler call suggests generic library search keywords and a theme ground
  colour. With personalize on, one more call merges the three into a final
  composition, photo objects are cropped and sent to Meshy Image-to-3D, and
  text props to Meshy Text-to-3D; filler keywords are always looked up on
  Poly Pizza (a Claude pick chooses the best search hit). With personalize
  off (the default), Meshy is skipped and the whole world, landmarks
  included, comes from the library. Every provider is behind an interface
  with a deterministic mock as the default, so no API keys are required.
- `GET /assets/{task_id}` + `GET /assets/{task_id}/model.glb` for Unity to
  poll and download every mesh — generated or retrieved — through the
  backend.
- Shared `schemas/world_recipe.schema.json` contract, with matching Pydantic
  models (backend) and C# DTOs (Unity).
- Unity: comic-book lobby/upload screens (file picker, description field,
  sky preset, personalize toggle), backend client, deterministic world
  generation (one loop track with hills and a drop, a placeholder
  environment composed from each object's `placement` hint, pass-through
  boost/stop/spin obstacles, an indoor room for the `indoor` sky), a
  Generating screen that waits for meshes behind a timeout, generated-mesh
  swap-in via glTFast, cel-shaded + Ghibli-graded rendering with cartoon
  particles and speed lines, two-player keyboard input, split-screen chase
  cameras, laps/checkpoints/results with a per-player HUD, comic SFX and a
  mood-matched soundtrack, and an offline fallback world if generation
  fails.

See `CLAUDE.md` for the full in/out-of-scope checklist.

## Architecture

```
Image + Text (+ sky, personalize) ──▶ AI backend ──▶ WorldRecipe (JSON) ──▶ Unity ──▶ Loop Track + placeholder props ──▶ 2P Split-Screen Race
                                        │  VLM objects → crops → Meshy image-to-3D  ┐                       ▲
                                        │  text props  → Meshy text-to-3D          ├ async mesh tasks       │
                                        │  filler keywords → Poly Pizza search     ┘                       │
                                        └──────────── GET /assets/{task_id} → GLB swapped in over placeholders ┘
```

AI decides WHAT the world is (data + meshes); Unity decides HOW to build it
(code). Details: `docs/architecture.md`, `docs/world-recipe.md`, and the
ADRs under `docs/decisions/`.

## Repository structure

```
/CLAUDE.md              — architecture rules, scope
/schemas/                — WorldRecipe JSON Schema (the contract)
/docs/                   — architecture, WorldRecipe reference, dev workflow, ADRs
/backend/                — FastAPI AI service (mock by default; Claude, Meshy, Poly Pizza optional)
/tools/                  — helper scripts (e.g. tools/sfx/fetch_sfx.py fetches the CC0 SFX)
/unity/                  — Unity project (Assets/Scripts/{Core,AI,Input,Players,Camera,
                            Racing,World,Assets,UI,Audio,Rendering}, Assets/Editor/
                            MainSceneBuilder.cs, runtime shaders under Assets/Resources/
                            Shaders and Assets/Shaders/MarioKart, CC0 audio/fonts under
                            Assets/Resources)
```

## Tech stack

- **Backend:** Python 3.11+, FastAPI, Pydantic v2, Pillow, `anthropic` SDK (Claude vision / text / merge / match-picker calls), httpx (Meshy, Poly Pizza), pytest
- **Frontend:** Unity 6 (6000.0.23f1), Built-in Render Pipeline, C#, Newtonsoft.Json, glTFast (runtime GLB import)

## Backend setup

```bash
cd backend
python3 -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt
cp .env.example .env
```

## Running the backend

```bash
cd backend && source .venv/bin/activate
uvicorn app.main:app --reload --port 8000
```

```bash
curl -F "image=@photo.jpg" -F "description=chaotic tropical paradise" \
  -F "sky=sunset" -F "personalize=true" \
  http://localhost:8000/generate-world
```

`sky` is one of `sunny | cloudy | sunset | night | indoor` (default `sunny`);
`personalize` defaults to `false`.

By default every provider is mocked. To run the real pipeline, set in
`backend/.env` (see `backend/.env.example` for every knob):

```
AI_PROVIDER=claude            ANTHROPIC_API_KEY=...   # vision: objects, mood, surface
TEXT_ASSET_PROVIDER=claude                            # key props from the description
FILLER_ASSET_PROVIDER=claude                          # generic keywords + ground colour
ASSET_MERGE_PROVIDER=claude                           # final composition (personalize only)
MESH_PROVIDER=meshy           MESHY_API_KEY=...       # image/text-to-3D (personalize only)
LIBRARY_PROVIDER=polypizza    POLYPIZZA_API_KEY=...   # filler retrieval
MATCH_PICKER_PROVIDER=claude                          # or `heuristic` (free, no network)
```

With `personalize=true`, each photo object (max `MAX_OBJECTS_PER_WORLD`,
default 4) and text prop (max `MAX_TEXT_ASSETS_PER_WORLD`, default 2) costs
Meshy credits and takes minutes; the Generating screen waits for every mesh
with no timeout when personalize is on (racing on placeholders would defeat
the point), and behind `GameConfig.meshWaitTimeoutSeconds` otherwise, with
placeholders standing in for anything that fails. Library lookups are free
and resolve at once.

## Unity setup

Open `unity/` in Unity Hub with Unity 6000.0.x. `Assets/Scenes/Main.unity`
is committed but is (re)built by the **MarioKart → Build Main Scene** menu
item (`Assets/Editor/MainSceneBuilder.cs`), which is the source of truth for
the hierarchy and Inspector wiring — run it after pulling changes to that
file. See `docs/development.md` for the setup checklist and the wiring
table.

## Development workflow

- Don't change `schemas/world_recipe.schema.json` without updating both
  `backend/app/models/world_recipe.py` and
  `unity/Assets/Scripts/AI/WorldRecipe.cs` in the same change.
- Run `pytest` in `backend/` before opening a PR that touches the backend.
- See `docs/development.md` for full conventions and the Unity checklist.
