# Architecture

## Core principle

> **AI decides WHAT the world should be. Unity decides HOW to construct it.**

The AI backend never manipulates Unity objects, and Unity never calls an AI
model directly. The only thing that crosses the boundary is a versioned
`WorldRecipe` JSON document (see [`world-recipe.md`](world-recipe.md)).

## Components

```
┌─────────────────────────┐        HTTP POST /generate-world        ┌───────────────────────────┐
│   Unity (client)        │ ─────────────────────────────────────▶ │   Backend (FastAPI)        │
│                         │        multipart: image + description  │                            │
│  UI (ImageUploadUI)     │                                         │  api/generate_world.py     │
│  AI (WorldRecipeClient) │ ◀───────────────────────────────────── │  services/VisionService    │
│  Core (GameManager)     │        { "world_recipe": {...} }        │  services/WorldSynthesisSvc│
│  World (WorldGenerator, │                                         │  models/WorldRecipe        │
│    TrackGenerator,      │                                         │                            │
│    EnvironmentGenerator)│                                         └───────────────────────────┘
│  Assets (AssetResolver) │
│  Players/Racing/Camera  │
└─────────────────────────┘
```

- **VisionService** (backend): image bytes → `SceneUnderstanding` (dominant colors, brightness, tags). Mocked in bootstrap; behind an interface so a real vendor can be swapped in later.
- **WorldSynthesisService** (backend): `SceneUnderstanding` + description → `WorldRecipe`. Mocked in bootstrap; same swap-ability.
- **WorldRecipeClient** (Unity): the only network call Unity makes. POSTs the single world input, parses the response into a `WorldRecipe`, or reports an error.
- **GameManager** (Unity): owns the `GameState` machine and orchestrates the flow between UI, the backend call, and world generation.
- **WorldGenerator / TrackGenerator / EnvironmentGenerator** (Unity): turn a `WorldRecipe` into an actual scene — one loop track, checkpoints, and placeholder environment objects, all deterministic from `recipe.seed`.
- **AssetResolver** (Unity): maps a recipe object type string to a placeholder visual today, and to a real asset provider later, without `EnvironmentGenerator` changing.

## One race, sequence

```
Boot ── Start ──▶ Input ── pick image + type description ──▶ Generating
  │                                                               │
  │                                          POST /generate-world │
  │                                                               ▼
  │                                                      Backend returns
  │                                                      WorldRecipe (or errors)
  │                                                               │
  │                              success ─────────┬────── failure │
  │                                                ▼               ▼
  │                                     WorldGenerator.Generate(recipe)
  │                                     (failure uses DefaultWorldRecipe)
  │                                                │
  │                                                ▼
  │                                          WorldReady ──▶ Countdown ──▶ Racing
  │                                                                          │
  │                                            both players cross the line ─┘
  │                                                                          ▼
  └───────────────────────────────── Play Again ◀── Results ◀── Finished ◀──┘
```

## Why no networking beyond the one HTTP call

This is a local, same-keyboard 2-player game. There is no online multiplayer,
no matchmaking, and no state synchronization beyond the single request/response
that fetches the `WorldRecipe`. Anything resembling network game state is out
of scope — see `CLAUDE.md`'s scope checklist.

## Determinism

Every procedural system derives its randomness from `recipe.seed` via
`WorldRandom.DeriveSeed`, never from `UnityEngine.Random`'s global state, so
the same `WorldRecipe` always produces the same track and environment. See
`docs/decisions/0004-seed-derivation.md`.
