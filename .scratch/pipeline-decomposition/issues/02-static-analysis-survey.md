# 02 — Survey static-analysis tools and generic discovery

Labels: `wayfinder:research`

Blocked by: (none)

## Question

For the quality step to run the *repo's own* configured analysers (generic, not hardcoded):

- How to discover configured static analysis in an arbitrary repo (npm scripts, eslint/stylelint config, dotnet analysers, Vue lint, etc.).
- What tool families are worth including in v1 vs deferring (lint/autofix, complexity/CRAP, security, dependency updates).
- Which tools emit machine-parseable output with autofixes (→ AFK candidates) vs advisory-only (→ HITL candidates).

Deliverable: findings captured on a throwaway `research/quality-tools` branch, linked here.

## Resolution (closed)

See [02-findings.md](02-findings.md). Key: repo has nothing configured -> discovery must degrade gracefully. Discovery order: package.json scripts (lint/format/typecheck) > config files > dependency > SDK default. v1 set: repo-configured eslint/stylelint, tsc, dotnet format + Roslyn, advisory dep-vuln scan. AFK = parseable + safe autofix (eslint --fix, stylelint --fix, prettier, dotnet format); HITL = advisory (tsc, Roslyn, npm audit). SARIF as unifying output; confirm AFK by re-run.
