# 02 — Deepen row mapping behind a RunStore

**What to build:** Fold the read-side row mapping into a deep `RunStore` module with a small interface — `All()` and `Active()` — that hides the SQL and the reader-to-record mapping.

**Problem:** Reads are a shallow, fragile hand mapping spanning three coupled things in `Program.cs:243–277`: the `runColumns` string, 15 positional reader indexes in `QueryRuns`, and the 15-field `Run` record. Column order must stay aligned across all three by eye; a drift breaks silently. Interface is nearly as wide as the implementation.

**Solution:** A `RunStore` module exposes `All()` and `Active()`; the column list, query, and mapping live once inside. The two `MapGet` handlers call the store. This also gives the fold (issue 01) a place to persist through, and makes an in-memory adapter possible for end-to-end fold tests.

**Blocked by:** 01 — Deepen the run-event fold. *(Not strictly required, but sequencing after 01 lets the store also absorb the fold's persist step and justify a second, in-memory adapter — one adapter today is only a hypothetical seam.)*

**Status:** todo

- [ ] `RunStore` exposes `All()` (all runs, newest-first) and `Active()` (`launching`/`running`/`stalled`, newest-first).
- [ ] Column list, SQL, and reader-to-record mapping exist in exactly one place inside the module.
- [ ] `GET /runs` and `GET /runs/active` return identical JSON to today.
- [ ] Mapping mismatch (column/field drift) surfaces as a single loud failure, not a silent wrong value.
