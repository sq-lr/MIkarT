# Backend — World Recipe Generation API

FastAPI service that turns one image + one text description into a `WorldRecipe`
JSON document (see `../schemas/world_recipe.schema.json`). Ships with a mock
AI implementation so it runs with **no external API keys**.

## Setup

```bash
cd backend
python3 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
cp .env.example .env
```

## Run

```bash
uvicorn app.main:app --reload --port 8000
```

## Try it

```bash
curl -F "image=@/path/to/photo.jpg" -F "description=chaotic tropical paradise" \
  http://localhost:8000/generate-world
```

Response:

```json
{ "world_recipe": { "version": 1, "seed": 123, "world": { ... }, "track": { ... }, "objects": [...], "palette": [...] } }
```

## Test

```bash
pytest
```

Tests cover: `WorldRecipe` model validation (clamping, seed defaulting), the
`/generate-world` endpoint returning a schema-valid recipe, and determinism of
the mock synthesis service.

## Architecture

- `app/api/generate_world.py` — the one HTTP route.
- `app/models/` — Pydantic models mirroring `schemas/world_recipe.schema.json`.
- `app/services/vision_service.py` — image → `SceneUnderstanding` (mocked).
- `app/services/world_synthesis_service.py` — scene + description → `WorldRecipe` (mocked).
- `app/services/asset_service.py` — placeholder hook for a future external asset lookup; unused by the endpoint today.
- `app/prompts/world_generation.py` — prompt text for a future real-LLM `WorldSynthesisService`; unused by the mock.

Swapping the mock for a real vision/LLM provider means writing a new class
behind the existing `VisionService`/`WorldSynthesisService` interfaces — the
API contract (`POST /generate-world` → `{"world_recipe": {...}}`) does not change.
