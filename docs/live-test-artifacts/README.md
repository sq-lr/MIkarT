# Live pipeline test artifacts

Manual, one-off outputs from exercising the real (non-mock) providers by
hand, kept for reference. Not fixtures used by `pytest` — those stay
self-contained under `backend/tests/`.

- `stage2-generate-world-response.json` — `POST /generate-world` response
  with `AI_PROVIDER=claude` (real Claude vision call) and `MESH_PROVIDER=mock`
  (no mesh jobs submitted), using `unity/Assets/Resources/sample_world_image.png`
  and description `"chaotic tropical paradise"`. Confirms real VLM detections
  (`wire_mesh_sculpture`, `bare_tree`, `information_kiosk`, `concrete_building`)
  flow correctly into `WorldRecipe.objects[]`; `world`/`track`/`palette` are
  still the deterministic mock synthesis output.
- `stage3-generate-world-response.json` — same call with `MESH_PROVIDER=meshy`
  too: each object carries a real `asset.task_id` from Meshy Image-to-3D.
  Submission is sequential (one HTTP round-trip per crop, see
  `app/api/generate_world.py`'s `for crop in crops` loop), so this response
  took ~4 successive Meshy submit calls to build.
- `meshes/*.glb` — the 4 real GLBs downloaded via `GET /assets/{task_id}/model.glb`
  once each Meshy task reached `status: ready` (confirmed via `file`: valid
  glTF binary v2). Generation took ~2–9 minutes per object once submitted.
  These are real assets Unity's `GeneratedMeshLoader`/glTFast import can be
  tested against without needing a live backend or Meshy call.
- `stage3-parallel-generate-world-response.json` / `stage3-parallel-timing.json` —
  a second live run of the same call, after `app/api/generate_world.py`'s
  crop-submission loop was changed to submit all crops to Meshy concurrently
  (`ThreadPoolExecutor`) instead of one at a time. `stage3-parallel-timing.json`
  has the measured per-stage breakdown from that run:
  - `vision.analyze_image`: 5.61s (the one Claude call)
  - `crop_objects` / `synthesis.synthesize`: negligible (<0.1s combined)
  - `mesh.submit` per crop: 0.69–0.85s each, but only **0.85s wall-clock**
    since they ran concurrently (vs. 2.98s if summed sequentially) — the
    `/generate-world` response itself took 6.49s total, dominated by the
    Claude call, not mesh submission.
  - `mesh_ready_seconds`: per-object time from submission to `ready` —
    still resolved roughly one at a time (216s, 233s, 463s, 510s), meaning
    actual mesh *generation* throughput is bottlenecked by Meshy's own
    queue/concurrency limits on this account, not by our submission code.
- `meshes-parallel/*.glb` — the 4 GLBs from that same parallel-submission run
  (independent Meshy outputs from `meshes/*.glb`, same 4 object types).

  **Note:** mesh submission (crops and, later, text assets) was subsequently
  reverted back to sequential, one at a time — the current
  `app/api/generate_world.py` no longer uses a `ThreadPoolExecutor` for mesh
  submission. The vision call and the text-asset-extraction call are the
  only two things still run concurrently (they're independent Claude calls
  with no data dependency). The artifacts above are a historical record of
  the now-reverted parallel-submission experiment, not the current behavior.

- `stage4-text-to-3d-response.json` / `meshes-text-to-3d/driftwood_whale_statue.glb` —
  live test of the text-extracted key-assets feature
  (`docs/decisions/0007-text-to-3d-key-assets.md`): `AI_PROVIDER=mock` (to
  isolate cost — the mock's canned `palm_tree`/`rock` objects don't depend on
  real vision), `TEXT_ASSET_PROVIDER=claude`, `MESH_PROVIDER=meshy`,
  description `"a chaotic tropical paradise with a giant floating whale
  statue made of driftwood"`. Claude correctly extracted only the
  description's distinctive prop (`driftwood_whale_statue`, `density: 0.12`)
  without duplicating the mock's `palm_tree`/`rock` — no dedup collision
  needed here since the labels didn't overlap. Its real Meshy Text-to-3D
  task went through the full preview→refine cycle transparently behind one
  task_id: polling showed `progress` jump straight to `50%` on the very
  first check (preview had already succeeded and refine was auto-submitted
  by `MeshyMeshGenerationService._get_text_status`), then climb smoothly
  50→100% over ~110s as refine ran, confirming the state machine's
  phase-hiding and progress-blending work against the real API, not just
  mocks. The resulting (refined/textured) GLB is valid binary glTF v2 but
  notably large (~19.5MB vs. 2.9–4MB for the image-to-3d ones).

  `driftwood_whale_statue_preview_untextured.glb` — the untextured PREVIEW
  mesh for the same task, fetched directly from Meshy's API afterward (no
  extra credits: it re-queries the already-completed preview task rather
  than submitting a new one; `MeshyMeshGenerationService.fetch_model()`
  itself only ever exposes the refined result, so this bypassed the app and
  hit Meshy's `GET /openapi/v2/text-to-3d/{preview_task_id}` directly). It's
  **16.3MB on its own, before any texture is added** — so most of the size
  here is geometry, not texture. `2k` (the value used) is already Meshy's
  *lowest* `texture_resolution` option (`2k`/`4k`/`8k`, no smaller choice
  exists), and `standard` is already the lowest `geometry_resolution`
  option too — so neither knob has more room to shrink the file; the size
  is inherent to how much polygon/vertex detail Meshy's `standard` preset
  produces for a prompt this elaborate (a good multi-clause prompt from the
  extraction step, but possibly worth testing whether a shorter/simpler
  `prompt` yields a smaller mesh).

  **Cost note:** since `MESH_PROVIDER` applies to *all* submissions, not
  just text ones, this run also submitted the mock vision's `palm_tree`/
  `rock` crops to real Meshy Image-to-3D as a side effect — ~4 Meshy jobs
  total (2 image-to-3d + 1 preview + 1 refine), not just the ~2 the text
  path alone would have cost. Isolating text-to-3d cost alone would need a
  way to skip image submission entirely (e.g. a temporary
  `MAX_OBJECTS_PER_WORLD=0`), which wasn't done here.

- `image-to-3d-merge.txt` — live test of the personalize=true final-
  composition merge (`docs/decisions/0014-asset-merge-final-composition.md`):
  `AI_PROVIDER=claude`, `TEXT_ASSET_PROVIDER=claude`,
  `FILLER_ASSET_PROVIDER=claude`, `ASSET_MERGE_PROVIDER=claude`, no mesh/
  library submission. Ran `object_extraction`, `text_asset_extraction`, and
  `filler_asset_extraction` (`max_landmarks=0`, as personalize=true always
  passes) against `unity/Assets/Resources/tree_image.jpg` + description
  `"jungle"`, then `ClaudeAssetMergeService.merge()` over all 15 combined
  candidates (3 photo, 0 text, 12 filler); each console line is annotated
  with which of the three sources produced it (`source=photo/text/filler`),
  the same field the merge call itself reasons over. Confirms the merge
  works end-to-end against the real API: it picked the one filler
  `"background"` candidate as-is, correctly preferred the photo-grounded
  `broadleaf_tree` over any filler candidate for the single `"landmark"`
  slot (filler can't even propose one when personalize=true), and dropped 5
  of the 15 candidates (`vine`, `tree_stump`, `log`, `torch`,
  `wooden_crate`) as redundant against the jungle-floor vegetation and
  roadside props it kept instead — real curation, not just relabeling.
  (Claude's extraction/merge calls aren't deterministic — a rerun landed on
  slightly different candidates/drops than the first pass, both valid.)
