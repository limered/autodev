# 03 — CRAP / complexity thresholds and the in-class vs out-of-class boundary

Labels: `wayfinder:research`

Blocked by: (none)

## Question

CRAP/complexity fixes split into in-class (safe-ish AFK) vs cross-class (HITL). Determine:

- What CRAP/complexity thresholds meaningfully separate "leave it" / "fix AFK" / "flag HITL".
- Where the in-class vs out-of-class boundary falls, and how a step detects which a given fix would require.
- How thresholds should be expressed in `AGENTS.md` config.

These need empirical tuning — capture initial recommended values and the tuning method. Findings on `research/complexity-thresholds` branch, linked here.

## Resolution (closed)

See [03-findings.md](03-findings.md). Key: leave-it CRAP<=30 & comp<=10; fix-AFK CRAP<=60 or comp<=15; flag-HITL CRAP>60 or comp>15 (harsher bucket wins). Never auto-fix below cov>=0.5. In-class vs cross-class decided by dry-run patch shape (1 file, no public-signature change, no new import = in-class/AFK). Flat quality.complexity YAML in AGENTS.md; tune via shadow-mode classifier logging.
