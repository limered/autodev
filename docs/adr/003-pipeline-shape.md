# ADR 003: Pipeline Shape Owned by AgentsConfig

## Status
Accepted

## Context
Pipeline shape (stage order, members, loop iterations) and slot identity
(which `(agent, iteration)` fills which category slot) were restated in four
Modules across three languages: `agents.json`, `lib/AgentsConfig.ps1`,
`lib/PhaseSteps.ps1` (`Get-PhaseStepCandidates` hardcoded 11 entries),
`infrastructure/multipass/test-feature-builder.sh`, plus API fold/status and
`RunView` fallbacks. Ordering drift fails silently as skipped rows.
Sources: `#192, #188, #171`.

## Decision
- `lib/AgentsConfig.ps1` owns pipeline shape, derived from the `agents.json`
  stages map. No new module file.
- `Get-PhaseStepCandidates` takes `$Config` and expands it: sequential emits
  one entry at iteration 0, loop emits N entries at 1..N, in map order.
  The hardcoded list is deleted.
- `Get-StepCategory` remains the slot rule. Overflow (pass index beyond
  sequential slots, e.g. `test-runner/5`) clamps to the last matching slot.
- `ConvertTo-SeededStages` stays one stage per category; iteration count only
  drives candidate expansion.
- `parallel` type is rejected; no coercion until a real need exists.
- Host adapters first (seeding + relay); VM phase order + API fold
  expectations follow once the Seam is pinned by Pester.

## Consequences
- One place to change stage order, members, or loop count.
- Pester pins ordering, iteration keys, and file names through the Seam
  instead of re-stating candidates per caller.
- Clamp overflow keeps detail under its category header; uncategorized bucket
  rendering is Batch 2 scope.
