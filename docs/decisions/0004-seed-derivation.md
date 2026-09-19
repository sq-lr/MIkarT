# 0004 — Seed derivation scheme

**Decision:**
1. The backend guarantees `WorldRecipe.seed` is always present and an
   integer — either from the AI, or deterministically derived from other
   recipe fields (`derive_seed` in `backend/app/models/world_recipe.py`) if
   missing. It is never randomized.
2. `WorldGenerator.Generate` derives `trackSeed = WorldRandom.DeriveSeed(recipe.seed, "track")`
   and `envSeed = WorldRandom.DeriveSeed(recipe.seed, "environment")`.
3. `EnvironmentGenerator` derives one further seed per object type:
   `WorldRandom.DeriveSeed(envSeed, entry.type)`.
4. All RNG consumption goes through `WorldRandom` (wraps `System.Random`),
   never `UnityEngine.Random`'s global state.

**Why:** "Same `WorldRecipe` + same seed = same generated world" needs to
hold end to end, including when the AI omits a seed, and including as new
object types are added to a recipe without perturbing existing ones (each
type's placement only depends on its own derived seed).

**Consequence:** Never call `UnityEngine.Random.*` in track/environment
generation code. Any new procedural system should derive its own seed label
via `WorldRandom.DeriveSeed(recipe.seed, "<system-name>")` rather than
reusing another system's seed.
