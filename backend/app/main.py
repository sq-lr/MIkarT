from dotenv import load_dotenv
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

# Populate os.environ from backend/.env (if present) before anything reads
# AI_PROVIDER / MESH_PROVIDER / API keys. Real env vars take precedence.
load_dotenv()

from app.api.assets import router as assets_router  # noqa: E402
from app.api.generate_world import router as generate_world_router  # noqa: E402

app = FastAPI(title="MIkarT World Generator", version="0.2.0")

# Permissive CORS for local development: Unity's UnityWebRequest running in
# the Editor/Player needs this to reach a locally-hosted backend.
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(generate_world_router)
app.include_router(assets_router)


@app.get("/health")
async def health() -> dict[str, str]:
    return {"status": "ok"}


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(app, host="0.0.0.0", port=8000)
