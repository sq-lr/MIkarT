# 0011 — Optional Claude-based semantic matching for Poly Pizza search results

**Decision:** Which Poly Pizza search result (if any) `PolyPizzaLibraryAssetService.submit`
uses is delegated to a new `MatchPickerService` interface, with two
implementations selected by `MATCH_PICKER_PROVIDER` (default `claude`):

1. `ClaudeMatchPickerService` (default, `MATCH_PICKER_PROVIDER=claude`): one
   Claude call per keyword, given the keyword plus every candidate's
   `title`, `tags`, and `category` (a numbered list), asking it to pick
   whichever ONE candidate is the best semantic match, or say none match.
   Uses `claude-haiku-4-5` rather than the Opus model the rest of this
   pipeline uses — this is a small, fast classification decision over a
   short list, not open-ended photo/text understanding, and it can run once
   per filler keyword (up to `MAX_FILLER_ASSETS_PER_WORLD` times per
   request), so latency/cost matter more here than anywhere else in the
   pipeline. Requires `ANTHROPIC_API_KEY`.
2. `HeuristicMatchPickerService` (`MATCH_PICKER_PROVIDER=heuristic`): the
   same free word-overlap scoring `_best_match` used to do inline (see ADR
   0009's matching fix) — score every candidate by how many of the
   keyword's words appear as a substring in its title, take the highest,
   zero everywhere means no match. No network, no key. Also the
   zero-dependency default used whenever a `PolyPizzaLibraryAssetService` is
   constructed without an explicit `match_picker` (direct/test
   construction) — only `providers.build_from_env()`'s env-driven default
   is `claude`.
3. `PolyPizzaLibraryAssetService` now returns `Tags`/`Category` from the raw
   search result (previously only `ID`/`Title`/`Download` were read) so a
   `MatchPickerService` has enough to reason about beyond the title alone.
   No schema or `WorldRecipe` change — these fields are only used at match
   time, never stored.

**Why this exists — the heuristic's real ceiling:** scoring can only ever
pick among candidates that share a literal word with the keyword. Two bugs
seen live proved this isn't just a tuning problem: for keyword
`"clock_tower"`, Poly Pizza has clocks and it has towers but never a
"clock tower" — so the heuristic could only guess between them, and picked
"Analog Clock" over "Bell Tower" (a genuinely better fit) purely by result
order, not understanding. For `"bicycle_rack"`, the correct-ish match
("Bike Rack") doesn't even contain the word "bicycle" — a synonym gap no
substring heuristic can bridge. Both are things a semantic pick trivially
gets right (confirmed live — see Consequences).

**Why an interface with two implementations instead of just replacing the
heuristic outright:** the heuristic is free and instant; Claude is neither.
Keeping it as a real, selectable implementation (rather than deleting it)
means `LIBRARY_PROVIDER=polypizza` still works with zero extra Claude calls
and no `ANTHROPIC_API_KEY` when that tradeoff is preferred
(`MATCH_PICKER_PROVIDER=heuristic`), and it's what a
`PolyPizzaLibraryAssetService` falls back to when constructed directly
without a `match_picker` (e.g. in tests) — nothing about retrieval itself
requires Claude at the type level.

**Why `MATCH_PICKER_PROVIDER` defaults to `claude`, unlike every other real
provider in this pipeline (which defaults to a zero-cost mock):** this
provider is only ever consulted at all once `LIBRARY_PROVIDER=polypizza` has
already been explicitly turned on — an offline demo or `pytest` run never
reaches it regardless of this default, since `LIBRARY_PROVIDER` itself
still defaults to `mock`. Once retrieval is on, match quality is the whole
point of the feature, and the heuristic's two known failure modes (no
shared words at all; a synonym the two sides don't spell the same way) are
common enough in a small public catalog that the extra per-keyword Claude
call is worth defaulting to for whoever already opted into real retrieval.

**Why this doesn't loosen `filler_asset_extraction.py`'s existing
"prefer single-concept keywords" guidance:** that guidance is about search
*hit-rate* (a keyword with zero results is zero results no matter how smart
the matcher is), not just match *precision* — orthogonal to what this
decision fixes. A semantic matcher makes it *safe* for a compound keyword
to occasionally slip through and still resolve correctly, but doesn't make
compound keywords more likely to return results in the first place, so the
prompt is left as-is.

**Why pick by numbered index into the candidate list instead of asking
Claude to return Poly Pizza's own `ID` string:** an index is trivially
validated (`0 <= i < len(candidates)`) with no failure mode; a copied-back
ID string could be malformed, truncated, or simply wrong, and would need
the same set-membership check anyway for zero benefit.

**Consequences:**
- Live-verified against the real Poly Pizza + Claude APIs on the exact
  regressions this fixes: `clock_tower` now matches "Bell Tower" (heuristic:
  "Analog Clock"); `bicycle_rack` now matches "Bike Rack" (heuristic:
  "Bicycle"); `trash_can`/`street_lamp`/`fountain` still match correctly
  under both pickers.
- One additional sequential Claude call per filler keyword when
  `MATCH_PICKER_PROVIDER=claude` — for a full `MAX_FILLER_ASSETS_PER_WORLD`
  (12) worth of keywords, that's up to 12 extra round-trips added to
  `/generate-world`'s response time, since mesh/library submission stays
  sequential (see ADR 0009 and this session's earlier explicit reversion of
  parallel mesh submission). Not parallelized here as a result; worth
  revisiting if this becomes a real latency problem.
- A wrong or unparseable Claude response degrades to "no match" (index
  `null`, out-of-range, or a refusal all return `None`), never a crash and
  never a forced bad pick — consistent with "a wrong mesh is worse than no
  mesh" everywhere else in this pipeline.
- No new credential surface: `MATCH_PICKER_PROVIDER=claude` reuses
  `ANTHROPIC_API_KEY`.
