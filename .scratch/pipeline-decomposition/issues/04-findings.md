# 04 — Findings: which refactorings are safe AFK

Prototype artifact for `04-safe-afk-refactorings.md`. React to this; the line moves where you say.

This is the **per-refactoring prior** — the fast pre-filter the quality step applies *before* generating a patch. Ticket 03 already gave the ground-truth mechanism (dry-run patch shape); this table is the cheap first pass so the fixer only bothers dry-running the refactorings that could plausibly be AFK.

## Classification table

Three classes: **AFK** (fixer applies, in-run), **HITL** (always a `ready-for-human` ticket), **CONDITIONAL** (AFK only if the dry-run patch passes the ticket-03 shape gate: 1 file, no public-signature change, no new import, coverage ≥ 0.5).

| Refactoring | Class | Why |
|---|---|---|
| Rename **local variable / private member** | AFK | Blast radius = one scope, compiler/LSP-verifiable, reversible |
| **Dead-code delete** (unreferenced private symbol) | AFK | No callers → no behaviour change; LSP find-refs == 0 is the proof |
| **Extract method** (new *private* method, same class) | AFK | Canonical in-class move; adds only a private symbol |
| Introduce **guard clause / early return** | AFK | Pure control-flow flattening inside one function |
| **Decompose conditional** (extract boolean into named local) | AFK | In-function, no signature touch |
| Inline a **single-use private** local/method | AFK | In-class, reduces symbols |
| **Reduce params** by bundling into existing local object | CONDITIONAL | If the method is public → signature change → HITL. Private-only → AFK |
| **Extract method to a NEW public/exported** method | CONDITIONAL | New public surface = API change; patch-shape gate decides |
| Rename a **public/exported** symbol | HITL | Callers outside the file break; cross-file, needs human judgement of the name |
| **Move method** to another class | HITL | Multi-file, changes two types' public shape |
| **Extract class** / split type | HITL | New type, new dependency edges, design decision |
| **Introduce parameter object** (new type) | HITL | New public type + every caller changes |
| **Change signature** (arity/type/visibility) of public member | HITL | Ripples to all callers; behaviour-adjacent |
| Reshape an **interface / abstract member** | HITL | Contract change; a seam decision, exactly what architecture step owns |
| Lower **CRAP by restructuring across methods** | HITL | comp>15 zone (ticket 03) — control flow genuinely hard, regression risk |

**Rule the step applies per finding:**
1. Look up the refactoring kind → AFK / HITL / CONDITIONAL.
2. HITL → write `ready-for-human` issue, don't touch code.
3. AFK / CONDITIONAL → produce dry-run patch → apply the ticket-03 shape gate (1 file, no public-signature delta, no new import, cov ≥ 0.5). Pass → apply. Fail → downgrade to HITL.

The declared kind is the fast filter; **the patch-shape gate is the ground truth and always wins.** A refactoring "typed" AFK that produces a multi-file patch is HITL.

## Worked example 1 — AFK (real code)

`dashboard/src/web/src/RunView/models/runView.js`: `runView()` holds three nested helpers (`freshnessClass`, `formatTime`, `stageStatusClass`). Say a complexity finding flags `runView` and the fixer proposes **extract-method** on `freshnessClass` into a module-private `function freshnessClass(...)` at file scope.

- Kind: extract-method → **AFK-eligible**.
- Dry-run patch: touches **1 file**; `runView`'s exported signature **unchanged**; adds a **non-exported** function; **no new import**.
- Gate: passes → **apply AFK**. Test surface (`runView.test.js`) still exercises the public `runView` output, so a regression would be caught.

## Worked example 2 — HITL (real code)

Same file: suppose the fixer instead proposes moving the three helpers into a new **exported** `runFormatting.js` module (extract-class / move-method) to "reduce runView's size".

- Kind: extract-class → **HITL** by table.
- Even if attempted, dry-run patch: **2 files** (new module + edited import), **new import edge** added.
- Gate: fails on files-touched and new-import → **HITL**. Writes a `ready-for-human` issue: "runView.js helpers could move to a shared formatting module — design call."

This is the right split: the mechanical flatten lands automatically; the module-boundary decision waits for a human — which is exactly the architecture step's job.

## Open knobs for you

1. **CONDITIONAL default when uncertain** — if the fixer can't confidently classify the kind, default to **HITL** (safe) or **attempt dry-run then gate** (more AFK throughput)? Lean: HITL — cheaper to under-fix than to ship a wrong refactor.
2. **Rename public symbol** — I put it HITL. LSP could rename-with-all-refs safely across files in a typed repo. Want typed-language public rename promoted to CONDITIONAL (AFK if LSP resolves every ref in-repo)? Or keep it flatly HITL for v1 simplicity?
3. Does this table live in the **quality agent's prompt**, or as a referenced doc it reads? (Contract ticket 01 said the loop is bash-owned, agent one-shot — the table is the agent's instruction either way.)
