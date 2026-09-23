# Triage labels

The `triage` skill uses the default canonical label vocabulary:

- `needs-triage`
- `needs-info`
- `ready-for-agent`
- `ready-for-human`
- `wontfix`

## Provenance labels

Alongside the triage states, provenance labels record which factory step
filed an issue:

- `agentic-review` — findings filed by the `agentic-review` agent (one
  `ready-for-human` architecture issue per run). Advisory: these
  findings never block a PR.
