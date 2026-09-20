# 0009 — Fill out the world with generic props retrieved from Poly Pizza, not generated

**Decision:** A third, independent Claude call suggests generic filler
props to diversify and fill out the world, retrieved from a public asset
library instead of generated:

1. A new `FillerAssetService` (`ClaudeFillerAssetService` when
   `FILLER_ASSET_PROVIDER=claude`) makes one more photo+text Claude call,
   alongside vision and text-asset extraction, and returns up to
   `MAX_FILLER_ASSETS_PER_WORLD` `filler_assets`: generic, common props
   (rocks, benches, traffic cones, crates) that would plausibly exist in a
   public 3D library, each with a `keyword` (a search term, not a generation
   prompt), a `density`, and a `placement` restricted to `roadside`,
   `background`, or `scattered` -- never `landmark`, since filler is
   generic, interchangeable clutter by definition.
2. Each keyword is searched via `LibraryAssetService`
   (`PolyPizzaLibraryAssetService` when `LIBRARY_PROVIDER=polypizza`) against
   Poly Pizza's keyword search API. A keyword with no match is dropped
   entirely -- unlike a photo/text object, a filler entry with nothing found
   has nothing worth keeping a placeholder for.
3. Poly Pizza retrieval is synchronous (no generation job to poll), but
   still implements the exact same `submit`/`get_status`/`fetch_model`
   contract as `MeshGenerationService`: `submit` resolves the search
   immediately, and `get_status` reports `ready` right away. This lets
   Unity's `GeneratedMeshLoader` poll it exactly like a Meshy task with
   **zero new Unity-side code** -- it already can't tell the two apart.
4. Since Meshy tasks and Poly Pizza tasks share one `task_id` namespace (the
   registry), `GET /assets/{task_id}` now looks up which provider registered
   a given task and dispatches to `providers.mesh` or `providers.library`
   accordingly, instead of always assuming Meshy.
5. `WorldSynthesisService.synthesize` appends filler objects last, after
   photo- and text-derived ones, deduping on label collision against both
   (photo and text both win over filler) and truncating at the schema's
   12-object cap same as before -- so filler is naturally the first thing
   dropped if a world is already full.
6. `ObjectAsset.provider`'s enum gains `"polypizza"` alongside `"meshy"` --
   the one schema change this decision requires, since Poly Pizza is a
   genuinely different vendor underneath (unlike text-to-3d, which stayed
   `"meshy"` because it was still Meshy's own API). Unity's `ObjectAsset`
   never inspects `provider` as anything but an opaque string, so this has
   no Unity-side impact despite touching the shared schema file.

**Why suggest keywords instead of generating filler meshes too:** the whole
point is diversifying and filling out the world *cheaply* -- rocks and
benches don't need to be personalized to the player's photo the way a
landmark or a named text object does. Generating them would cost the same
Meshy credits and multi-minute wait as any other object for content that's
explicitly meant to be generic. A free, instant library search is the right
cost profile for content whose entire value proposition is "good enough
filler," not "reflects your specific photo."

**Why a separate Claude call instead of reusing vision or text-asset
extraction's output:** those two calls are tuned to find *specific*,
worth-generating things -- vision explicitly favors distinctive photo
objects, text extraction explicitly favors vivid, unique named things. Their
prompts actively steer away from generic nouns. Asking either one to also
produce a second, contradictory kind of output (specific *and* generic in
the same call) would fight its own instructions; a dedicated call with the
opposite instruction ("prefer boring, common, searchable terms") is more
reliable than trying to split one call's attention.

**Why keywords must be simple/generic, unlike the vivid prompts the other
two extraction steps produce:** a text-to-3D generation prompt wants
maximum, specific visual detail; a library keyword search wants the
opposite -- a narrow, over-specific query returns no results at all. The
prompt explicitly inverts the other two extraction prompts' guidance for
this reason.

**Why `MAX_FILLER_ASSETS_PER_WORLD` defaults to the same value as
`MAX_ASSETS_PER_WORLD` (12) instead of a small fixed number:** filler's whole
job is to top up whatever room photo and text objects didn't already use, so
capping the request itself at a small number (an earlier default was 4)
defeated that purpose whenever fewer than 8 photo/text objects existed --
filler could never fill the remaining space even though nothing else was
competing for it. Since a filler request is one free search per keyword, the
default now asks for as many as could possibly fit (the same overall cap),
and lets the merge's existing dedup/truncation logic (see below) throw away
whatever doesn't end up fitting. `MAX_ASSETS_PER_WORLD` itself is also newly
exposed as its own env var (previously a private constant), read by
`synthesize()` and clamped to the schema's hard `max_length=12` regardless of
what it's set to, so a misconfigured value can't produce an invalid recipe.

**Why reuse the Meshy-shaped submit/status/fetch contract for something
synchronous:** the alternative would be a second, different Unity-side
delivery path (e.g. embedding retrieval results directly in the
`WorldRecipe` response, or a new endpoint), which is real new Unity work for
no benefit -- `GeneratedMeshLoader` already handles "poll until ready, then
download" correctly regardless of how fast "ready" arrives. Making the fast
path irrelevant to Unity, rather than teaching Unity a second path, keeps
this entirely backend-only.

**Why filler is dropped (not kept with a bare placeholder) when nothing is
found:** a photo/text object without an `asset` still keeps a *meaningful*
primitive placeholder (a decently-heuristic-matched shape for a real object
the player's input called for). A filler keyword is arbitrary and
disposable by design -- keeping a random generic placeholder for a keyword
that didn't even resolve to anything real adds visual noise without the one
thing filler was for (a real varied mesh), so it's simpler and more honest
to just drop it.

**Consequences:**
- One more Claude call per `/generate-world` request (run concurrently with
  the other two, so it doesn't add to response latency on its own).
- `GET /assets/{task_id}` now needs the registry lookup before it can decide
  which service to call -- a small but real coupling between the asset API
  and the registry that didn't exist before (previously it always assumed
  `providers.mesh`).
- Filler success is capped by the library's actual catalog: an overly
  specific or unusual keyword simply returns nothing, by design.
- `LIBRARY_PROVIDER=polypizza` requires its own `POLYPIZZA_API_KEY` (free,
  separate signup from Anthropic/Meshy) -- a new credential surface, unlike
  the text-to-3d work which reused an existing one.
- No attribution/licensing metadata (Poly Pizza's `Attribution`/`Licence`
  fields) is surfaced anywhere yet -- captured here as a known gap, not
  solved by this decision.
