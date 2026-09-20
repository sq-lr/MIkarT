# Backend — World Recipe Generation API

FastAPI service that turns one image + one text description into a `WorldRecipe`
JSON document (see `../schemas/world_recipe.schema.json`), and turns the
objects a vision model finds in the photo into 3D meshes via Meshy
Image-to-3D. Ships with mock providers so it runs with **no external API keys**.

## Setup

```bash
cd backend
python3 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
cp .env.example .env
```

`.env` selects the providers. Defaults are mocks; for the real pipeline:

| Variable | Values | Notes |
|---|---|---|
| `AI_PROVIDER` | `mock` (default), `claude` | Vision: scene tags/colors + object bounding boxes. `claude` uses `claude-opus-5` via the `anthropic` SDK and needs `ANTHROPIC_API_KEY`. |
| `MESH_PROVIDER` | `mock` (default), `meshy` | Mesh generation from object crops. `meshy` needs `MESHY_API_KEY`; `MESHY_MODEL_TYPE` (`standard`), `MESHY_SHOULD_TEXTURE` and `MESHY_TARGET_POLYCOUNT` (`8000`) tune the request; the model is pinned to `meshy-7.1`. |
| `MAX_OBJECTS_PER_WORLD` | int (default 4) | Cap on detected objects → Meshy tasks per world. |

## Run

```bash
uvicorn app.main:app --reload --port 8000
```

## Try it

```bash
curl -F "image=@/path/to/photo.jpg" -F "description=chaotic tropical paradise" \
  http://localhost:8000/generate-world
```

Response (returned immediately — meshes are still generating):

```json
{ "world_recipe": { "version": 1, "seed": 123, "world": { ... }, "track": { ... },
                    "objects": [ { "type": "palm_tree", "density": 0.5, "asset": { "task_id": "…", "provider": "meshy" } }, ... ],
                    "palette": [...] } }
```

Then poll / download each mesh:

```bash
curl http://localhost:8000/assets/<task_id>                # {"status":"pending|ready|failed","progress":..}
curl -o palm.glb http://localhost:8000/assets/<task_id>/model.glb   # 409 until ready, 404 if unknown
```

In mock mode no `asset` handles are issued (Unity keeps primitive placeholders).

## Test

```bash
pytest
```

Tests cover: `WorldRecipe` model validation (clamping, seed defaulting, the
optional `asset`), the `/generate-world` endpoint returning a schema-valid
recipe (incl. undecodable images and mesh-submit failures), object cropping,
the `/assets` lifecycle against a fake mesh service, the Meshy client against
an `httpx.MockTransport`, and `ClaudeVisionService` against a stub client.
No test makes a network call.

## Architecture

- `app/api/generate_world.py` — `POST /generate-world`: vision → crop → mesh submit → recipe.
- `app/api/assets.py` — `GET /assets/{task_id}` and `GET /assets/{task_id}/model.glb` (GLB proxied from the provider).
- `app/models/` — Pydantic models mirroring `schemas/world_recipe.schema.json`.
- `app/services/vision_service.py` — image + description → `SceneUnderstanding` incl. `detected_objects` (`MockVisionService`, `ClaudeVisionService`).
- `app/services/object_cropper.py` — bounding boxes → PNG crops (Pillow).
- `app/services/mesh_generation_service.py` — crop → async mesh task (`MockMeshGenerationService`, `MeshyMeshGenerationService`) + the in-memory `MeshTaskRegistry`.
- `app/services/world_synthesis_service.py` — scene + description + mesh tasks → `WorldRecipe` (deterministic mock).
- `app/services/providers.py` — env-driven wiring of the above; `override()` for tests.
- `app/prompts/object_extraction.py` — the Claude vision prompt.
- `app/prompts/world_generation.py` — prompt text for a future real-LLM `WorldSynthesisService`; unused by the mock.

Adding another vision or mesh vendor means a new class behind
`VisionService` / `MeshGenerationService` and a branch in `providers.py` —
the API contract does not change.
