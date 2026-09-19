# ChromaKart *(placeholder name)*

A local 2-player split-screen kart racer where one photo and one sentence
generate the world you race in.

## Current features (bootstrap stage)

- `POST /generate-world` FastAPI endpoint that turns an image + description
  into a `WorldRecipe` JSON document. A vision model finds the important
  objects in the photo, the backend crops them out, and each crop is sent to
  Meshy Image-to-3D to become a mesh. Real providers (Claude vision, Meshy)
  are behind interfaces; deterministic mocks are the default so no API keys
  are required.
- `GET /assets/{task_id}` + `GET /assets/{task_id}/model.glb` for Unity to
  poll and download the generated meshes after the race has already started.
- Shared `schemas/world_recipe.schema.json` contract, with matching Pydantic
  models (backend) and C# DTOs (Unity).
- Unity scaffolding for the full flow: image upload, backend client, world
  generation (one loop track + placeholder environment), generated-mesh
  swap-in via glTFast, two-player keyboard input, split-screen cameras,
  laps/checkpoints/race results, and an offline fallback world if generation
  fails.

This is a bootstrap: interfaces, schemas, mocks, and unverified-in-Editor
Unity scripts. See `CLAUDE.md` for the full in/out-of-scope checklist.

## Architecture

```
Image + Text ──▶ AI backend ──▶ WorldRecipe (JSON) ──▶ Unity ──▶ Loop Track + placeholder props ──▶ 2P Split-Screen Race
                   │  VLM finds objects → crops → Meshy image-to-3D (async)          ▲
                   └──────────── GET /assets/{task_id} → GLB swapped in over placeholders ┘
```

AI decides WHAT the world is (data + generated meshes); Unity decides HOW to
build it (code). Details: `docs/architecture.md`, `docs/world-recipe.md`,
`docs/decisions/0006-meshy-async-mesh-generation.md`.

## Repository structure

```
/CLAUDE.md              — architecture rules, scope, module ownership
/schemas/                — WorldRecipe JSON Schema (the contract)
/docs/                   — architecture, WorldRecipe reference, dev workflow, ADRs
/backend/                — FastAPI AI service (mock by default; Claude vision + Meshy optional)
/unity/                  — Unity project (Assets/Scripts/{Core,AI,Input,Players,
                            Camera,Racing,World,Assets,UI})
```

## Tech stack

- **Backend:** Python 3.11+, FastAPI, Pydantic v2, Pillow, `anthropic` SDK (Claude vision), httpx (Meshy), pytest
- **Frontend:** Unity 2022.3 LTS, Built-in Render Pipeline, C#, Newtonsoft.Json, glTFast (runtime GLB import)

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
  http://localhost:8000/generate-world
```

By default both providers are mocked (`AI_PROVIDER=mock`, `MESH_PROVIDER=mock`).
To generate real meshes from your photo, set in `backend/.env`:

```
AI_PROVIDER=claude      ANTHROPIC_API_KEY=...
MESH_PROVIDER=meshy     MESHY_API_KEY=...
```

Each detected object (max `MAX_OBJECTS_PER_WORLD`, default 4) costs Meshy
credits and takes minutes; Unity races on placeholders until they arrive.

## Unity setup

Open `unity/` in Unity Hub with Unity 2022.3.x LTS. No scene/prefab files
are committed yet (see `docs/decisions/0005-no-handauthored-unity-assets.md`)
— follow the one-time setup checklist in `docs/development.md` before you
can press Play.

## Development workflow

- Don't change `schemas/world_recipe.schema.json` without updating both
  `backend/app/models/world_recipe.py` and
  `unity/Assets/Scripts/AI/WorldRecipe.cs` in the same change.
- Run `pytest` in `backend/` before opening a PR that touches the backend.
- See `docs/development.md` for full conventions and the Unity checklist.

## Team responsibilities

| Owner | Area |
|---|---|
| Person A | AI / backend / WorldRecipe |
| Person B | Unity gameplay / track / racing |
| Person C | Assets / asset resolver / environment |
| Person D | UI / upload flow / QA |
