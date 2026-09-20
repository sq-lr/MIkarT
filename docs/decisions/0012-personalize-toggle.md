# 0012 — A "generate personalized assets" toggle, off by default, that skips Meshy entirely

**Decision:** The upload screen gets a new toggle, **"Generate personalized
assets," off by default**, sent to the backend as `personalize` on
`POST /generate-world`:

1. When **on** (opt-in): behavior is unchanged from before this decision --
   vision detects photo objects, text extraction finds named objects, both
   are submitted to Meshy (image-to-3D / text-to-3D), and filler suggests
   generic library-searchable props (never a landmark, since photo/text
   already supply up to `MAX_LANDMARKS`).
2. When **off** (the default): text extraction is never even called (its
   whole purpose is Meshy generation prompts), `crop_objects` never runs,
   and nothing is submitted to Meshy at all. The entire `objects[]` list is
   sourced from `FillerAssetService`/`LibraryAssetService` (Poly Pizza)
   instead. Since nothing else in this mode can supply a `landmark`,
   `FillerAssetService.suggest()` is called with `max_landmarks=MAX_LANDMARKS`
   instead of the usual `0`, using a prompt variant that allows the model to
   nominate 1-2 generic-but-plausible centrepieces (e.g. "lighthouse",
   "clock_tower", "fountain") rather than leaving the world without one.
3. `WorldSynthesisService.synthesize()` gained a `personalize: bool` flag
   that gates whether `scene.detected_objects`/`text_assets` contribute
   anything to `objects[]` at all -- when off, they're simply not converted,
   regardless of what `mesh_tasks`/`text_mesh_tasks` contain (which should
   be empty anyway, since nothing was submitted).
4. `FillerAsset.placement` widened from a 3-value enum (excluding
   `"landmark"`) to the full 4-value `Placement` type already used by
   `DetectedObject`/`ExtractedAsset`. The "no landmark unless allowed" rule
   moved from the Pydantic field type to `suggest()`'s own prompt +
   defense-in-depth demotion (mirroring the cross-source demotion already in
   `world_synthesis_service._objects_from_filler_assets`), since the
   restriction is now conditional on `max_landmarks`, not universal.

**Why off by default:** Meshy generation is the slow, costly part of this
pipeline (minutes per mesh, real credits) for the payoff of a world that
reflects the player's specific photo/description. Retrieval is instant and
free. Defaulting to the fast/free path and letting players opt into the
slower "wow" experience only when they want to wait for it is a better
default experience than always paying that cost up front.

**Why skip text extraction's Claude call entirely, not just its Meshy
submission:** `TextAssetService.extract()` exists purely to produce vivid,
specific generation prompts for Meshy Text-to-3D (see ADR 0007) -- its
output has no other use. Calling it and discarding the result would waste a
Claude call and add latency for nothing.

**Why let filler nominate a landmark instead of leaving the world without
one:** a `landmark` is meant to give the player a centrepiece to steer by
(see `EnvironmentGenerator.PlaceLandmarks`). Without personalized
generation, nothing else in the pipeline can produce one -- rather than
special-case "no landmark this world," it's simpler and more consistent to
let the same retrieval mechanism that supplies everything else in this mode
also supply the landmark, just from a prompt that steers toward generic
things that still plausibly read as centrepieces (a lighthouse, a fountain)
rather than photo-specific ones.

**Why widen `FillerAsset.placement` at the type level instead of a separate
"landmark-allowed" variant model:** the shape is identical either way (same
three fields); only the *allowed values* differ per call, which a runtime
parameter (`max_landmarks`) already expresses. A second near-duplicate
Pydantic model would just be the same data shape with narrower validation,
without adding real safety -- the demotion logic already re-validates the
count regardless of what the model alone would allow.

**Consequences:**
- `personalize=False` (the default) requests still make 2 concurrent Claude
  calls (vision + filler) plus one Poly Pizza search per suggested keyword
  -- cheaper and faster than the personalized path's 3 Claude calls plus
  Meshy generation, but not literally free.
- A world's landmark, when personalization is off, is whatever generic term
  Claude judged to fit *and* Poly Pizza's library actually has -- it may not
  exist at all if nothing plausible comes to mind or nothing is found,
  which is an acceptable, honest degradation (same "if not found, drop it"
  philosophy as any other filler keyword).
- Existing callers of `WorldSynthesisService.synthesize()` default
  `personalize=True`, so behavior is unchanged unless a caller opts in to
  the new parameter -- this decision doesn't retroactively change what
  already-shipped code paths do.
