# 0006 — Generate meshes from the photo (Meshy) instead of retrieving assets, delivered asynchronously

**Decision:** The environment's props are no longer looked up from an asset
registry by type name. Instead:

1. The vision model (`VisionService`; Claude vision when `AI_PROVIDER=claude`)
   returns, alongside scene colors/tags, the list of distinct objects in the
   uploaded photo with normalized bounding boxes (`SceneUnderstanding.detected_objects`).
2. `object_cropper.crop_objects` cuts each box out of the source image.
3. Each crop is submitted to Meshy Image-to-3D (`MeshyMeshGenerationService`
   when `MESH_PROVIDER=meshy`) as a base64 data URI. Meshy returns a task ID.
4. `/generate-world` returns **immediately**; each `objects[]` entry whose
   crop was submitted carries `asset: {task_id, provider}`.
5. Unity builds the world at once with primitive placeholders and starts the
   race. `GeneratedMeshLoader` polls `GET /assets/{task_id}` until `ready`,
   downloads `GET /assets/{task_id}/model.glb`, imports it with glTFast, and
   places a copy over every placeholder of that type (hiding the primitive's
   renderer, never moving it).

Mock providers remain the default: `MockVisionService` returns canned
detections so the crop pipeline runs offline, and `MockMeshGenerationService`
issues no tasks, so recipes carry no `asset` and Unity keeps its primitives.

**Why generation, not retrieval:** the point of the game is that *your*
photo becomes the track. A type-name lookup ("palm_tree" → generic palm) can
only ever reproduce a fixed vocabulary; image-to-3D reproduces the actual
lantern / totem / weird chair that was in the picture, and the VLM's label
vocabulary is open-ended.

**Why asynchronous, with placeholder swap:** Meshy takes on the order of
minutes per model. Blocking `/generate-world` on that would push the
"Generating" screen to several minutes and Unity's request timeout to ~10
min. Returning the recipe first keeps time-to-race unchanged and keeps rule 6
(the game survives AI failure) trivially true: a failed, slow, or mocked mesh
just means the placeholder stays. The cost is a second backend surface,
which amends CLAUDE.md rule 4 from "one call" to "Unity only talks to the
backend API".

**Why the backend proxies the GLB:** Unity fetching from Meshy's CDN would
mean a second external host in the client, Meshy's signed URLs expire, and
the API key would need to be near the client. Proxying keeps Unity ↔ backend
as the only edge and lets the backend cache each GLB in memory so retries
don't re-download.

**Why an in-memory task registry:** this is a local single-session game; a
backend restart implies a new world anyway. Persisting tasks/meshes is
explicitly out of scope (CLAUDE.md).

**Consequences:**
- `objects[].type` is now whatever label the VLM produced. `AssetResolver`
  no longer warns on unknown types; it picks a placeholder silhouette by
  keyword heuristic.
- `MAX_OBJECTS_PER_WORLD` (default 4) caps Meshy tasks per world: each is
  credits plus minutes of generation.
- `com.unity.cloud.gltfast` is a new Unity package dependency for runtime
  GLB import.
- Determinism is preserved: mesh copies adopt placeholder transforms that
  are derived from the seed; the mesh content itself is non-deterministic
  (Meshy) but never affects layout.
- A real LLM-based world synthesis (theme/palette/track from the same
  Claude call) is a natural next step but not part of this decision.
