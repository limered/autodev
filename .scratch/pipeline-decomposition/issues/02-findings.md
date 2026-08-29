# 02 — Static-analysis survey: findings

Research only. Grounded against this repo (`dashboard/src/web`, `dashboard/src/Api`).

## Reality check from this repo

- `dashboard/src/web/package.json` has **no lint script and no eslint dep** — only `test: vitest run`. No `.eslintrc*`, no `stylelint`.
- `dashboard/src/Api/Api.csproj` is a bare SDK-Web project — no `.editorconfig`, no analyzer packages, no `<AnalysisMode>`.

Lesson: **absence is the common case.** Discovery must degrade gracefully — a repo may have configured nothing. The quality step runs what's declared; if nothing is declared, it runs nothing and reports that, rather than hardcoding tools.

## 1. Discovery heuristics (generic, any repo)

Prefer **declared intent** (scripts/config the repo committed) over probing for installed binaries. Order:

**JS/TS/Vue**
- Parse `package.json` → `scripts`: match keys/values against `/\b(lint|format|stylelint|typecheck|tsc)\b/`. A declared `lint` script is the strongest signal — run it as-is.
- Config-file presence (implies eslint even without a script): `.eslintrc.{js,cjs,json,yml}`, `eslint.config.{js,mjs,cjs}` (flat), `.stylelintrc*`, `stylelint.config.*`, `prettier` config, `tsconfig.json` (→ `tsc --noEmit`).
- `devDependencies` containing `eslint`, `stylelint`, `prettier`, `typescript` corroborates.
- Vue: flat/legacy config referencing `eslint-plugin-vue` or `@vue/eslint-config-*`; `.vue` files are covered by the same eslint invocation.

**.NET**
- Solution/project discovery: `*.sln`, `**/*.csproj`.
- `.editorconfig` with `dotnet_diagnostic.*` / `dotnet_analyzer_*` rules → analyzers are configured; severity lives here.
- csproj signals: `<EnableNETAnalyzers>`, `<AnalysisMode>`, `<TreatWarningsAsErrors>`, `<CodeAnalysisRuleSet>`, or `<PackageReference>` to analyzer packages (StyleCop.Analyzers, SonarAnalyzer.CSharp, Roslynator, Meziantou.Analyzer).
- `dotnet format` availability is implied by any SDK project (built into the SDK), independent of analyzer packages.

**Heuristic priority:** declared script > committed config file > installed dependency > SDK built-in default. Emit a per-language "detected / not detected" manifest so the step is auditable.

## 2. Tool families — v1 vs defer

| Family | v1? | Why |
|---|---|---|
| **Lint + autofix** (eslint, stylelint, dotnet format) | **v1** | Deterministic, machine-parseable, safe autofix → the core AFK story. |
| **Type check** (tsc, C# build/nullable) | **v1** | Structured errors, high signal, no false-fix risk (HITL). |
| **Security — deps** (`npm audit`, `dotnet list package --vulnerable`) | **v1 (report-only)** | Cheap, JSON output. Fixes (`npm audit fix`) can break builds → HITL, don't auto-apply. |
| **Complexity / CRAP / coverage-weighted** | **defer** | No autofix, needs coverage data + thresholds, noisy without tuning. |
| **Dependency version bumps** (Dependabot/renovate style) | **defer** | Not a fault class; belongs to a maintenance workflow, not per-run quality. |
| **SAST security** (Semgrep, CodeQL, SonarQube) | **defer** | Heavy setup, advisory-only, org-specific rulesets. |

**Recommended v1 set:** repo-configured **eslint** (+ stylelint if present), **tsc**, **dotnet format** + configured Roslyn analyzers via `dotnet build`, plus **advisory** dependency-vuln scan. Everything else deferred.

## 3. Output & autofix per tool (AFK vs HITL)

**AFK candidates — machine-parseable AND real autofix:**
| Tool | Parse | Autofix |
|---|---|---|
| eslint | `eslint --format json` (or `--format sarif` via formatter) | `--fix` (only rules with fixers) |
| stylelint | `stylelint --formatter json` | `--fix` |
| prettier | n/a (all-or-nothing) | `prettier --write` / `--check` |
| dotnet format | `--report <dir>` emits JSON | `dotnet format` mutates in place; `--verify-no-changes` to detect |

**HITL candidates — parseable but advisory / unsafe to auto-apply:**
| Tool | Parse | Fix status |
|---|---|---|
| tsc | text diagnostics (`--pretty false`); no native JSON | no autofix |
| Roslyn analyzers (via `dotnet build`) | `-warnaserror` + `--getProperty`/binlog, or SARIF via `ErrorLog=<file>.sarif` | some have code fixes but only surface in IDE, not CLI-safe |
| roslynator | `roslynator analyze --output <xml>` | `roslynator fix` exists but broad → treat as HITL |
| npm audit | `npm audit --json` | `npm audit fix` may bump majors → HITL |
| dotnet list package --vulnerable | `--format json` (SDK 9+) else text | no autofix |
| SonarAnalyzer / Semgrep | SARIF | advisory only |

**Fix-boundary rule:** a fault is AFK only if the tool applied a fix *and re-running the same tool shows the fault gone*. Autofix that changes semantics (npm audit majors, roslynator bulk) is HITL regardless of a `--fix` flag existing.

**SARIF is the unifying format:** eslint (via `@microsoft/eslint-formatter-sarif`), Roslyn (`ErrorLog=*.sarif`), Semgrep, CodeQL all emit it. Normalise to SARIF where available, fall back to each tool's JSON, then to parsing text (tsc). Classify AFK/HITL after normalisation, not per-tool.
