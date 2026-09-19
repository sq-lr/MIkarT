# 0007 — Generate meshes from the text description too, via Meshy Text-to-3D

**Decision:** Object meshes no longer come only from the photo. A player's
text description can name distinctive props that aren't in the picture at
all (e.g. "with a giant floating whale statue"), so:

1. A new `TextAssetService` (`ClaudeTextAssetService` when
   `TEXT_ASSET_PROVIDER=claude`) reads the description alone and returns up
   to `MAX_TEXT_ASSETS_PER_WORLD` `key_assets`: distinctive props the words
   call for, each with a `label`, a vivid `prompt` (fed straight to Meshy),
   and a `density`.
2. Each extracted asset is submitted to Meshy **Text-to-3D**
   (`MeshyMeshGenerationService.submit_text`) instead of Image-to-3D.
   Meshy's real Text-to-3D API is a two-phase workflow — preview (untextured
   geometry) then refine (adds texture, referencing the preview's result) —
   unlike Image-to-3D's single submit. `submit_text` kicks off only the
   preview and returns its task_id as the one public id for the asset's
   whole lifecycle; `get_status`/`fetch_model` detect "preview succeeded"
   and submit refine themselves, so the two phases are invisible outside
   `MeshyMeshGenerationService`.
3. `/generate-world` submits crops and text assets to Meshy one at a time
   (sequential, matching the original image-crop submission), but runs the
   vision call and the text-extraction call concurrently, since those two
   Claude calls have no data dependency on each other.
4. `WorldSynthesisService.synthesize` merges text-derived objects after
   photo-derived ones, skipping any text label that collides with a
   photo-detected label, and truncates to `WorldRecipe.objects`'s 12-item
   cap if the combined count ever exceeds it.
5. No schema change: `ObjectAsset.provider` stays `"meshy"` whether the mesh
   came from Image-to-3D or Text-to-3D — it's the same vendor either way.

**Why a separate Claude call instead of extending the vision call:** the
vision call is scoped to what's visible in the photo — it returns bounding
boxes so `object_cropper` can cut pixels out of the image. A prop the
player only *described* has no pixels to crop and no bounding box, so it
structurally can't be a `DetectedObject`. Keeping this a second, independent
call keeps each call's contract honest: one call only ever describes what's
actually in the image, the other only ever reads the words.

**Why hide preview→refine behind one task_id instead of exposing both
phases:** `MeshTaskRegistry`, `/assets/{task_id}`, and Unity's
`GeneratedMeshLoader` all assume exactly one id per generating object,
polled until `ready`. Exposing preview and refine as two visible phases
would mean either two `MeshTask`s per text asset (breaking the
one-id-per-object shape the schema and registry both assume) or teaching
Unity a new intermediate state — both larger, riskier changes than doing
the phase transition server-side inside `MeshyMeshGenerationService`, where
a per-task lock (`_TextTaskState.refine_lock`) also prevents two concurrent
pollers from submitting a duplicate, wasted refine job.

**Why a separate `MAX_TEXT_ASSETS_PER_WORLD` cap instead of folding into
`MAX_OBJECTS_PER_WORLD`:** each text asset costs **two** Meshy jobs
(preview + refine), not one like an image crop. Sharing one cap would make
the knob misleading — an operator raising `MAX_OBJECTS_PER_WORLD` wouldn't
obviously know the text-asset slice of that budget is twice as expensive
per unit. A distinct env var (default `2`, vs. `MAX_OBJECTS_PER_WORLD`'s
default `4`) makes the cost asymmetry visible and independently tunable.

**Why photo-derived wins on a label collision:** the photo is ground truth
for what's literally in the player's picture. Text extraction never sees
the photo, so it can't know it's about to name something already detected;
on a collision, the photo-derived entry is more likely the accurate one,
and skipping the text duplicate avoids spending a second, redundant Meshy
job on (likely) the same conceptual object.

**Consequences:**
- Each text asset in flight costs 2 Meshy jobs; `MAX_TEXT_ASSETS_PER_WORLD`
  defaults low (`2`) to keep this comparable to, not additive on top of,
  the photo path's cost.
- `MeshyMeshGenerationService` now carries extra per-task state (a
  `task_id -> _TextTaskState` dict plus a per-task lock) beyond the
  near-stateless image-to-3d path; `MockMeshGenerationService`'s
  `submit_text` stays a one-line no-op, same as `submit`.
- `WorldRecipe.objects[]`'s 12-item cap is now shared between two
  independent sources; a misconfigured combination of caps silently
  truncates trailing text-derived entries (logged), rather than raising a
  validation error out of the endpoint.
- `TEXT_ASSET_PROVIDER=claude` reuses `ANTHROPIC_API_KEY` — no new
  credential surface.
- Ground texture and skybox generation from the text description are
  explicitly **not** part of this decision — only individual decoration
  props. That remains future work, same as real LLM-based world synthesis
  was called out as "a natural next step but not part of" ADR 0006.
