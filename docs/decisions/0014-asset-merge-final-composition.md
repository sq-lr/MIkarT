# 0014 — LLM-based final asset-composition merge for personalize=true

**Decision:** When `personalize=True`, a new `AssetMergeService` call runs
once, right after the photo/text/filler extraction calls all finish and
before anything is submitted to Meshy or the Poly Pizza library, to decide
the world's actual final composition:

1. `app/prompts/asset_merge.py` + `app/services/asset_merge_service.py`
   (`AssetMergeService` ABC, `MockAssetMergeService` default,
   `ClaudeAssetMergeService` for `ASSET_MERGE_PROVIDER=claude`) — a single
   structured-output call over the combined candidate pool from
   `scene.detected_objects` (photo), `key_assets` (text), and
   `filler_candidates` (filler), each flattened to a `MergeCandidate{index,
   source, label, placement, density}`. The response is a `MergeDecision
   {index, keep, placement}` per candidate, referenced by index only — the
   same "pick by index, never ask the model to echo data back" pattern as
   `MatchPickerService` (ADR 0011). Uses `claude-haiku-4-5`, not the Opus
   model the three extraction calls use, for the same reason ADR 0011's
   match picker does: a short, already-fully-specified classification/
   reconciliation decision, not open-ended photo/text understanding.
2. `generate_world.py`'s new `_merge_assets()` helper flattens the three
   source lists into indexed candidates, calls the service, then filters
   and re-places each source's own list to match the decisions — every
   other field (`bbox`, `prompt`, `keyword`, ...) is left untouched, since
   only the source that produced an object knows how to submit it for a
   mesh. Runs only when `personalize=True`; `personalize=False` sources the
   whole world from filler alone (ADR 0012), which already stages its own
   single background/landmark/fill-the-rest composition in one call, so
   there's nothing to merge across sources.
3. `ClaudeAssetMergeService` still enforces the hard invariants itself
   (exactly one `"background"`, at most `MAX_LANDMARKS` `"landmark"`s) as a
   defense-in-depth pass over whatever the model returns, demoting any
   excess to `"scattered"` in input order (photo before text before
   filler) — the same reasoning `world_synthesis_service`'s own per-source
   demotion already uses, just now also applied to a single Claude
   response instead of trusted outright.
4. `Providers` gains a required `asset_merge: AssetMergeService` field, and
   `providers.py` gains `ASSET_MERGE_PROVIDER` (`mock` default | `claude`),
   following the exact pattern of every other provider in this file.

**Why this exists:** `object_extraction.py`, `text_asset_extraction.py`,
and `filler_asset_extraction.py` each run as independent Claude calls (see
`generate_world.py`'s concurrent `ThreadPoolExecutor`) with no visibility
into what the other two propose. In `personalize=true` mode all three can
freely suggest `"background"` and `"landmark"` placements, so the combined
pool routinely has multiple competing candidates for those scarce slots.
Before this decision, `world_synthesis_service`'s `_objects_from_scene` /
`_objects_from_text_assets` / `_objects_from_filler_assets` reconciled this
the only way three independent per-source functions can: whichever source
ran first (photo, then text, then filler) claimed the landmark slots first,
and *every* source's `"background"`-tagged object was kept as a separate
wall variant with no judgment about which one actually fits the scene best.
That's order, not quality. This decision replaces that arbitration with one
more Claude call that sees the whole pool at once and can reason about
which candidate is genuinely the best background, which are the most
distinctive landmarks, and which candidates are redundant enough to drop
entirely.

**Why merge *before* mesh/library submission, not after:** a candidate the
merge rejects (an excess landmark, a near-duplicate, a low-priority filler
candidate over the cap) never costs a wasted Meshy generation or Poly Pizza
search. Running the reconciliation after submission would still fix the
final `WorldRecipe`'s composition, but at the cost of generating meshes for
objects that get thrown away — real money and real latency for nothing.

**Why the merge call can also drop a candidate outright (`keep=false`),
not just reassign its placement:** the naive per-source demotion this
replaces could only ever *relabel* an excess landmark to `"scattered"`, it
had no way to notice that two candidates from different sources describe
the same thing (e.g. photo's `"palm_tree"` and filler's `"palm_tree"` would
already collide-dedup by exact label, but a photo `"tree"` and a text
`"oak_tree"` would not) or that the combined pool simply has more
candidates than the world should use. Giving the merge step a real drop
decision, scoped by `max_assets` in the prompt, lets it curate quality
instead of just reshuffling placements.

**Why candidates are addressed by index instead of asking the model to
re-emit each object's full data:** identical reasoning to ADR 0011's match
picker — an index into a list Python already holds is trivially validated
(`in {c.index for c in candidates}`) with no failure mode, whereas asking
Claude to reproduce a `label`/`prompt`/`keyword` string back risks a subtly
altered echo silently diverging from the object that was actually submitted
for a mesh.

**Why this doesn't replace `world_synthesis_service`'s own per-source
demotion/truncation logic:** that logic remains the backstop for every
failure mode of this new call — a `MockAssetMergeService` (no network,
returns every candidate unchanged), a failed `ClaudeAssetMergeService` call
(`_merge_assets` catches any exception and falls back to each source's
list, untouched), or a `personalize=False` world (which never calls this
service at all) all still flow through the same `_objects_from_*` functions
afterward. This decision is a quality improvement layered in front of an
already-safe pipeline, not a replacement for its safety net.

**Consequences:**
- One additional sequential Claude call added to the `personalize=true`
  path, after the three concurrent extraction calls resolve and before
  crop/mesh/library submission begins — on the order of one more Haiku
  round-trip's latency, in line with ADR 0011's match picker accepting the
  same tradeoff for match quality.
- Live-verified against the real Claude + Meshy + Poly Pizza APIs: a
  village/castle/dragon-statue photo+description that would otherwise
  surface up to three independent `"background"` candidates and however
  many `"landmark"`s each source felt like proposing instead produced
  exactly one background, exactly two landmarks, and the rest correctly
  split across roadside/scattered.
- No new credential surface: `ASSET_MERGE_PROVIDER=claude` reuses
  `ANTHROPIC_API_KEY`.
- `Providers` gaining a required field means every direct `Providers(...)`
  construction across the test suite needed `asset_merge=` added — done in
  `tests/test_assets_api.py`'s four call sites, consistent with how every
  earlier required-field addition to this dataclass (e.g. ADR 0007's
  `text_assets`) was handled.

**Amendment — "background" is hardcoded to filler, not merge-decided:**
after the initial version shipped, `generate_world.py`'s `_merge_assets` was
changed to no longer let the merge call arbitrate `"background"` at all.
`filler_asset_extraction` always suggests exactly one `"background"`
candidate (its prompt's dedicated BACKGROUND step) — that's a guarantee no
photo/text detection has, since a VLM might tag a skyline object
`"background"` in one photo and nothing at all in another. Rather than
have the merge call pick the better of two candidates with very different
reliability, `_merge_assets` now demotes any photo/text `"background"` to
`"scattered"` and pulls filler's `"background"` candidate(s) out of the
pool entirely *before* building the candidate list — they never reach
`AssetMergeService.merge()`, and are spliced back into the result
unconditionally afterward. `app/prompts/asset_merge.py`'s prompt was
updated to match (no more BACKGROUND step; the model is told explicitly it
will never see one). `ClaudeAssetMergeService`'s `_sanitize` still contains
a `"background"`-uniqueness guard, kept as a pure backstop in case this
service is ever called some other way, but it's dead code on the normal
path now. Why not just leave it to the LLM as before: this removes an
entire failure mode (the merge preferring a shakier photo/text guess, or —
worse — a bad response leaving zero backgrounds) for a slot that already
had a reliable, purpose-built answer sitting right there, at zero cost
(nothing here waits on an LLM decision it didn't need to make).
