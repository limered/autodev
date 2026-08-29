# 03 — Findings: CRAP / complexity thresholds and the in-class vs cross-class boundary

Research only. No code changes. Answers ticket `03-complexity-thresholds.md`.

## 1. CRAP formula and threshold values

**CRAP (Change Risk Anti-Patterns)** for a method with cyclomatic complexity `comp`
and test coverage `cov` (0.0–1.0):

```
CRAP = comp^2 * (1 - cov)^3 + comp
```

Properties worth exploiting for the decision logic:

- At `cov = 1.0` (fully covered), `CRAP = comp` — coverage collapses the risk term entirely.
  A well-tested method is only ever "crappy" if its raw complexity is high.
- At `cov = 0.0`, `CRAP = comp^2 + comp` — untested complexity dominates.
- The lever an AFK fixer actually pulls is `comp`; the lever a human can also pull is `cov`.
  This is why coverage matters to the *routing* decision, not just the score.

**Commonly-cited conventions (industry defaults):**

| Metric | Convention | Source lineage |
|---|---|---|
| CRAP | `> 30` = "crappy", needs attention | Crap4J / original CRAP proposal |
| Cyclomatic complexity | `> 10` = review, `> 15` = refactor, `> 20`+ = high-risk | McCabe (10), NIST-235 allows up to 15 by exception |
| Coverage floor implied by CRAP=30 | comp 5 needs ~42% cov; comp 10 needs ~80% cov; comp 15 needs ~92% cov | derived from formula |

### Recommended starting bands (per-function)

Route each finding into one of three buckets using **both** CRAP and raw complexity,
taking the *more severe* bucket when they disagree (belt-and-suspenders):

```
leave-it:   CRAP <= 30  AND  comp <= 10
fix-AFK:    30 < CRAP <= 60   OR   10 < comp <= 15
flag-HITL:  CRAP > 60    OR   comp > 15
```

Rationale:
- **`leave-it` at CRAP≤30 / comp≤10** — the two canonical "healthy" lines. Below both,
  a refactor is churn risk with no payoff; YAGNI.
- **`fix-AFK` up to CRAP 60 / comp 15** — the "smelly but mechanical" zone. Fixes here are
  typically extract-method / guard-clause / early-return / decompose-conditional: bounded,
  in-function, low blast radius. CRAP 60 corresponds to e.g. comp 10 at ~63% cov or comp 8
  at ~50% cov — messy but tractable.
- **`flag-HITL` above** — comp > 15 means the control-flow graph is genuinely hard; an
  automated refactor risks changing behaviour. Kick to a human ticket.

**Hard AFK safety gate (independent of band):** never auto-apply a fix to a function whose
covered ratio is below a floor, because there is no test to catch a regression. Recommend
`cov >= 0.5` as the AFK gate — below it, even a `fix-AFK` band finding downgrades to HITL.
This directly uses the coverage term the CRAP formula already weights.

## 2. Detecting in-class vs cross-class fixes

The router must predict the *blast radius* of the fix before applying it. Signals, cheapest first:

**Strong in-class signals (favour AFK):**
- The refactor is **extract-method within the same class/file** — new private method, same
  file, no signature change to any public member. Detectable: proposed diff touches exactly
  one file and adds only private/local symbols.
- No change to any **public/exported** method signature (name, arity, types, visibility).
- No new imports / `using` / `require` added.
- Only local variables and existing instance fields are referenced.

**Cross-class / out-of-class signals (force HITL):**
- Proposed fix **adds a new class, moves a method to another class, or splits the type**
  (extract-class, move-method, introduce-parameter-object). These change the public shape.
- Diff spans **more than one file**, or edits a shared/base class or interface.
- **Public API surface changes**: signature, visibility, or an interface/abstract member —
  callers elsewhere are affected. Grep callers; if any live outside the file, it's cross-class.
- New dependency edge introduced (new import between modules).
- Touches a symbol with **references outside the current file** (LSP "find references" > 0
  external hits on the symbol being changed).

**Practical detection mechanism for the step:**
1. Let the fixer produce a *candidate patch* (dry-run), don't apply it.
2. Classify the patch mechanically:
   - files-touched == 1 **and** no public-signature line changed **and** no new import
     → **in-class → AFK-eligible**.
   - otherwise → **cross-class → HITL**.
3. Cross-check with the refactoring *kind* the fixer declares (extract-method =in-class;
   extract-class / move-method / change-signature = cross-class). The patch-shape check is
   the ground truth; the declared kind is a fast pre-filter.

This is generic across repos because it keys on patch shape + public-signature deltas, not
on any language-specific AST beyond "what is exported / public".

## 3. AGENTS.md config block (proposed schema)

Small, flat, overridable per-repo. Coverage gate and bands are the only knobs.

```yaml
quality:
  complexity:
    # per-function routing thresholds
    leave_it:            { crap_max: 30,  cyclomatic_max: 10 }
    fix_afk:             { crap_max: 60,  cyclomatic_max: 15 }   # above -> hitl
    # AFK safety gates (all must hold, else finding downgrades to hitl)
    afk_gate:
      min_coverage: 0.5          # no auto-fix on under-tested code
      in_class_only: true        # cross-class patches always -> hitl
      max_files_touched: 1
    routing: worst_of            # crap vs cyclomatic disagree -> take harsher bucket
```

Notes:
- `leave_it` / `fix_afk` upper bounds; anything past `fix_afk` is HITL by definition — no
  third block needed (keeps it minimal).
- `afk_gate` is the veto layer: a `fix_afk`-band finding still becomes HITL if any gate fails.
- Every value has a documented default so the block is optional; a repo only writes the keys
  it wants to override.

## 4. Empirical tuning method

Treat the thresholds as a classifier and measure it against real outcomes.

1. **Log every finding with features, not just the decision.** For each: `crap`, `cyclomatic`,
   `coverage`, `files_touched`, `public_signature_changed`, chosen `route`, and later the
   **outcome** (AFK fix merged clean? reverted? human rejected? tests broke in CI?).
2. **Run in shadow/HITL-heavy mode first.** For a bootstrap window, route borderline findings
   (say `30 < CRAP < 90`) to HITL *and* have the AFK fixer produce its candidate patch anyway.
   Humans accept/reject. This yields labelled data on where AFK *would* have been safe.
3. **Compute confusion outcomes per band:**
   - AFK fixes that landed clean = true positives.
   - AFK fixes reverted / broke CI = false positives → thresholds too loose, lower the band.
   - HITL findings a human fixed trivially in-class = false negatives → band too tight, raise it.
4. **Tune to a target AFK false-positive rate** (e.g. keep reverted-AFK < 5%). Raise
   `crap_max` / `cyclomatic_max` until FP rate approaches the ceiling, then stop.
5. **Calibrate the coverage gate separately:** bucket AFK outcomes by coverage decile; set
   `min_coverage` at the lowest decile where regression rate is still acceptable.
6. **Per-repo overrides emerge from data:** a repo with high coverage and good tests can raise
   `min_coverage` gate down / bands up; a legacy repo tightens them. Ship global defaults,
   let the logged outcomes justify each repo's override in its AGENTS.md.

Minimum viable telemetry to make this possible: persist the per-finding feature row + outcome.
Without the outcome column, no tuning is possible — that logging is the real prerequisite.
