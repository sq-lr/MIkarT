# ChromaKart *(placeholder name)*

A local 2-player split-screen kart racer where one photo and one sentence
generate the world you race in.

## Current features (bootstrap stage)

- `POST /generate-world` FastAPI endpoint that turns an image + description
  into a `WorldRecipe` JSON document, using a deterministic mock AI (no API
  keys required).
- Shared `schemas/world_recipe.schema.json` contract, with matching Pydantic
  models (backend) and C# DTOs (Unity).
- Unity scaffolding for the full flow: image upload, backend client, world
  generation (one loop track + placeholder environment), two-player
  keyboard input, split-screen cameras, laps/checkpoints/race results, and
  an offline fallback world if generation fails.

This is a bootstrap: interfaces, schemas, and placeholder implementations
only. See `CLAUDE.md` for the full in/out-of-scope checklist.

## Architecture

```
Image + Text ──▶ AI backend ──▶ WorldRecipe (JSON) ──▶ Unity ──▶ Loop Track + Environment ──▶ 2P Split-Screen Race
```

AI decides WHAT the world is (data); Unity decides HOW to build it (code).
Details: `docs/architecture.md`, `docs/world-recipe.md`.

## Repository structure

```
/CLAUDE.md              — architecture rules, scope, module ownership
/schemas/                — WorldRecipe JSON Schema (the contract)
/docs/                   — architecture, WorldRecipe reference, dev workflow, ADRs
/backend/                — FastAPI mock AI service
/unity/                  — Unity project (Assets/Scripts/{Core,AI,Input,Players,
                            Camera,Racing,World,Assets,UI})
```

## Tech stack

- **Backend:** Python 3.11+, FastAPI, Pydantic v2, pytest
- **Frontend:** Unity 2022.3 LTS, Built-in Render Pipeline, C#, Newtonsoft.Json

## Backend setup

```bash
cd backend
python3 -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt
cp .env.example .env
```

## Running the mock backend

```bash
cd backend && source .venv/bin/activate
uvicorn app.main:app --reload --port 8000
```

```bash
curl -F "image=@photo.jpg" -F "description=chaotic tropical paradise" \
  http://localhost:8000/generate-world
```

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
