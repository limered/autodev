# Static-analysis policy

Reference doc for the `static-analysis` agent (`.opencode/agents/static-analysis.md`).
The agent reads this at the start of every run and applies it mechanically.
**Thresholds, catalog and tables are tuned HERE, never in the agent prompt.**

Source decisions: wayfinder tickets 02 (tool survey), 03 (complexity thresholds),
04 (safe-AFK refactorings) of `pipeline-decomposition`.

## 1. Discovery heuristic

Discover the *repo's own* analysers. Order of authority, strongest first:

1. **declared script** — a script in a committed `package.json` whose key or value
   matches `/\b(lint|format|stylelint|typecheck|tsc)\b/`. The strongest signal: run it
   as-is when scanning.
2. **committed config** — a lint/typecheck config file the repo committed (list below).
3. **installed dependency** — the tool named in `devDependencies` (weak: corroborates a
   config; on its own, run the tool's default invocation).
4. **SDK built-in** — implied by the platform itself (e.g. any .NET SDK project implies
   `dotnet format`), independent of any explicit configuration.

Never run a tool the repo has not declared through one of these signals — **hardcode
nothing, fabricate nothing; absence is the common case** (a repo with nothing configured
reports `not_detected` for every candidate and produces zero findings).

Signals per surface:

**JS/TS/Vue** — for every committed `package.json` (skip any under a `node_modules`
directory), usually one per package folder (e.g. `dashboard/src/web`):

- `scripts` matching the regex above → declared script.
- Config files in that folder or the repo root: `eslint.config.{js,mjs,cjs}`,
  `.eslintrc.{js,cjs,json,yml}`, `.stylelintrc*`, `stylelint.config.*`,
  `.prettierrc*` / `prettier.config.*` → implies the matching tool
  (`tsconfig.json` → `tsc`).
- `devDependencies` containing `eslint`, `stylelint`, `prettier`, `typescript`.
- Vue is covered by the same eslint invocation (`.vue` files ride along).

**.NET** — any `*.sln` or `**/*.csproj` present:

- `dotnet-format`: implied by any SDK project (SDK built-in).
- `dotnet-build` (compiler + Roslyn diagnostics): **errors** are always findings;
  **warnings** are findings only when analyzers are configured — `.editorconfig` with
  `dotnet_diagnostic.*` / `dotnet_analyzer_*` rules, or csproj `<AnalysisMode>`,
  `<EnableNETAnalyzers>`, `<TreatWarningsAsErrors>`, `<CodeAnalysisRuleSet>`, or a
  `<PackageReference>` to an analyzer package (StyleCop, SonarAnalyzer, Roslynator,
  Meziantou).

**Security** — advisory, report-only: `npm-audit` when an npm lockfile exists;
`dotnet-vulnerable` when a .NET project exists.

**Complexity** — only a *declared* tool (a script or config naming a complexity/CRAP
analyser). There is no built-in complexity scanner in v1; if nothing is declared,
report the candidates as `not_detected` and emit no complexity findings.

The target repo's `AGENTS.md` `quality:` block (§7) may add or narrow tools and
overrides thresholds.

## 2. Known-analyser catalog

The catalog is the **interpretation layer** for declared signals, not a mandate: a
candidate runs only when the repo declared it via §1. Manifest names come from the
`name` column.

| name | family | detected by | report (no fix) | autofix | output |
|---|---|---|---|---|---|
| `eslint` | lint | script / config / devDep | `npx eslint --format json .` (SARIF via `--format @microsoft/eslint-formatter-sarif` only if that package is installed) | `npx eslint --fix .` | JSON `results[]` |
| `stylelint` | lint | script / config / devDep | `npx stylelint --formatter json "**/*.{css,scss,vue}"` | append `--fix` | JSON array |
| `prettier` | lint | script / config / devDep | `npx prettier --check .` | `npx prettier --write .` | file list |
| `tsc` | typecheck | `tsconfig.json` / `typescript` dep | `npx tsc --noEmit --pretty false` | — | text `TSxxxx` diagnostics |
| `dotnet-format` | lint | any .NET project | `dotnet format --verify-no-changes --report <tmpdir>` | `dotnet format` | JSON report |
| `dotnet-build` | typecheck | any .NET project | `dotnet build` (add `/p:ErrorLog=<tmp>.sarif` for SARIF) | — | SARIF or text |
| `npm-audit` | security | npm lockfile | `npm audit --json` | — (`npm audit fix` can bump majors → HITL) | JSON |
| `dotnet-vulnerable` | security | .NET project | `dotnet list package --vulnerable --format json` (older SDKs: plain text) | — | JSON / text |

Run JS tools from the package folder whose `package.json` declared them; run .NET
tools against the solution / project files found.

A declared script that names a tool **outside the catalog** (e.g. `lint: biome check .`)
still runs as-is — declared script wins. Parse its output generically: JSON if
parseable, else one finding per `file:line: message` text line; family `lint`;
classify per §3.

## 3. Classification (route × family)

`family` comes from the catalog column above (`lint | complexity | typecheck | security`).
`route` (`afk | hitl`) per family:

- **typecheck** → `hitl`. Advisory, no CLI-safe autofix (tsc, compiler/Roslyn errors).
- **security** → `hitl`. Report-only; `npm audit fix` may bump major versions.
- **lint** → `afk` **iff** the fault is mechanically fixable *and* the fix is verifiable
  by re-running the same tool:
  - the rule has a tool fixer → `fix.instruction` is the scoped fix command
    (e.g. `npx eslint --fix src/foo.ts`);
  - or the fix is a one-sentence mechanical, semantics-preserving edit (delete an
    unused symbol, fix an import path) → `fix.instruction` is that imperative edit;
  - a rule whose fixer already ran in the autofix pass and the fault **persists** →
    `hitl` (the tool had its chance);
  - anything needing judgement → `hitl`.
- **complexity** → thresholds in §4 + refactoring table in §5.

`fix` is present only when `route: afk`:

```json
{ "kind": "tool-autofix" | "refactor-directive", "instruction": "<imperative>" }
```

`tool-autofix` for lint (a command), `refactor-directive` for complexity (a directive).
The instruction is imperative — one command or one sentence the fixer executes exactly.

## 4. Complexity thresholds

Per-function, with cyclomatic complexity `comp` and test coverage `cov` (0.0–1.0):

```
CRAP = comp^2 * (1 - cov)^3 + comp
```

Route by the **worst** bucket when CRAP and comp disagree:

```
leave-it:   CRAP <= 30  AND  comp <= 10   → drop entirely (not a finding)
fix-afk:    CRAP <= 60   OR   comp <= 15  → afk candidate (§5 table decides)
flag-hitl:  CRAP >  60    OR   comp > 15  → hitl
```

**AFK safety gates** (all must hold, else a fix-afk band finding downgrades to `hitl`):

- coverage `cov >= 0.5` — never auto-fix under-tested code;
- in-class only — the patch touches exactly 1 file, changes no public/exported
  signature, adds no new import.

A complexity fault reported without comp/CRAP numbers is unclassifiable → `hitl`.

## 5. Safe-AFK refactoring table

The declared refactoring **kind** is the fast pre-filter; the **patch-shape gate**
(1 file, no public-signature delta, no new import, `cov >= 0.5`) is ground truth and
always wins.

| Refactoring | Class | Why |
|---|---|---|
| Rename **local variable / private member** | AFK | Blast radius = one scope, verifiable, reversible |
| **Dead-code delete** (unreferenced private symbol) | AFK | No callers → no behaviour change |
| **Extract method** (new *private* method, same class) | AFK | Canonical in-class move |
| Introduce **guard clause / early return** | AFK | Pure control-flow flattening inside one function |
| **Decompose conditional** (extract boolean into named local) | AFK | In-function, no signature touch |
| Inline a **single-use private** local/method | AFK | In-class, reduces symbols |
| **Reduce params** by bundling into existing local object | CONDITIONAL | Public method → signature change → HITL; private-only → AFK |
| **Extract method to a NEW public/exported** method | CONDITIONAL | New public surface; patch-shape gate decides |
| Rename a **public/exported** symbol | HITL | Callers outside the file break (flatly HITL for v1) |
| **Move method** to another class | HITL | Multi-file, changes two types' public shape |
| **Extract class** / split type | HITL | New type, new dependency edges, design decision |
| **Introduce parameter object** (new type) | HITL | New public type + every caller changes |
| **Change signature** of public member | HITL | Ripples to all callers |
| Reshape an **interface / abstract member** | HITL | Contract/seam change — human territory |
| Lower **CRAP by restructuring across methods** | HITL | comp>15 zone — regression risk |

Rules the agent applies per complexity finding:

1. Look up the kind → AFK / HITL / CONDITIONAL. Unclassifiable kind → **default HITL**
   (cheaper to under-fix than to ship a wrong refactor).
2. HITL → roll-up issue; never touch code.
3. AFK / CONDITIONAL → `fix.instruction` is the directive, and it **must carry the
   shape constraint**: "must stay within one file, no public/exported signature change,
   no new imports; if impossible, leave the code unchanged". A fixer that cannot meet
   the constraint leaves the code alone → the finding survives the fix pass →
   self-escalation (agent contract, step 7) routes it to HITL on the next scan.

## 6. Finding identity

```
id = tool:rule:file:line
```

- `tool` — the manifest name from §2.
- `rule` — the ruleId / diagnostic code (eslint rule, `TSxxxx`, Roslyn CAxxxx;
  `npm-audit` uses the GHSA advisory id; `prettier` uses `format`).
- `file` — repo-relative path as the tool reported it (dependency findings: the
  package name or project file).
- `line` — reported start line, `0` when not applicable (dependency/prettier findings).

The id is stable across a run: the same fault next scan yields the same id. A refactor
that shifts line numbers mints a new id — acceptable; stale ids age out because only
currently-reported ids are compared for self-escalation. Truncate `message` to ~500
characters.

## 7. AGENTS.md config surface (target repo)

The target repo overrides defaults with a `quality:` YAML block in its `AGENTS.md`
(fenced ```yaml``` or plain indented YAML). Every key has a default, so the block is
optional; a repo writes only the keys it wants to override. `tools.add` /
`tools.skip` narrow or extend discovery for that repo only.

```yaml
quality:
  tools:
    add: []        # e.g. ["biome"] — extra declared analysers to run
    skip: []       # e.g. ["npm-audit"] — detected tools to not run this repo
  complexity:
    leave_it: { crap_max: 30, cyclomatic_max: 10 }
    fix_afk:  { crap_max: 60, cyclomatic_max: 15 }   # above -> hitl
    afk_gate:
      min_coverage: 0.5        # no auto-fix on under-tested code
      in_class_only: true      # cross-class patches always -> hitl
      max_files_touched: 1
    routing: worst_of          # crap vs cyclomatic disagree -> harsher bucket
```

## 8. Tool behaviour notes

- **Exit codes**: eslint/stylelint `0` clean, `1` findings, `>=2` fatal (config) —
  fatal is a *tool error* to log in the run summary, never findings. `npm audit`
  non-zero IS findings (vulnerability bitmask). `tsc` non-zero = findings.
  `dotnet build` non-zero = parse the diagnostics as findings. `dotnet format
  --verify-no-changes` non-zero = formatting findings.
- **Restore**: if a JS tool cannot run because `node_modules` is missing, run `npm ci`
  once in that package folder, then retry. .NET tools restore themselves. Never commit
  install side effects.
- **SARIF ingest**: normalise `runs[].results[]` → `tool` = the SARIF driver name,
  `rule` = `ruleId`, `file` = `artifactLocation.uri`, `line` = `region.startLine`,
  `message` = `message.text`. SARIF where available, each tool's JSON next, text last.
- **Baseline → autofix → re-scan**: a fault is only counted as fixed when its id is
  absent from the re-scan (fix-verified, not fix-assumed).
