---
name: improve-codebase-architecture
description: Scan a codebase for deepening opportunities and report them as a flat list of candidate cards. Read-only exploration - no report file, no grilling, no repo writes.
disable-model-invocation: true
---

# Improve Codebase Architecture

Surface architectural friction and propose **deepening opportunities** — refactors that turn shallow modules into deep ones. The aim is testability and AI-navigability.

This skill is a single **Explore** pass. It is read-only: it writes nothing into the repo, opens no report file, and asks no questions. Its whole output is a flat list of candidate cards for a human to triage later.

This skill is _informed_ by the project's domain model and built on a shared design vocabulary:

- Run the `/codebase-design` skill for the architecture vocabulary (**module**, **interface**, **depth**, **seam**, **adapter**, **leverage**, **locality**) and its principles (the deletion test, "the interface is the test surface", "one adapter = hypothetical seam, two = real"). Use these terms exactly in every candidate — don't drift into "component," "service," "API," or "boundary."
- The domain language in `CONTEXT.md` gives names to good seams; ADRs in `docs/adr/` record decisions this skill should not re-litigate.

## Process

Explore, then emit candidate cards. That is the whole process — there is no report step and no grilling step.

### Explore

**Scope before you scan — YAGNI.** Deepening a module pays off by making future changes to it easier, so put extra weight on the parts of the codebase that have recently changed. Decide *where* to look before you look:

- If a direction was named — a module, a subsystem, a pain point — take it, and skip the inference below.
- Otherwise, walk back a good stretch of the commit history (`git log --oneline`) to find the codebase's hot spots — the files and areas that keep coming up — and let those paths pull your attention first. If the changes are scattered with no clear hot spot, widen the net.

Read the project's domain glossary (`CONTEXT.md`) and any ADRs in the area you're touching first.

Then spawn a sub-agent to walk the codebase. Don't follow rigid heuristics — explore organically and note where you experience friction:

- Where does understanding one concept require bouncing between many small modules?
- Where are modules **shallow** — interface nearly as complex as the implementation?
- Where have pure functions been extracted just for testability, but the real bugs hide in how they're called (no **locality**)?
- Where do tightly-coupled modules leak across their seams?
- Which parts of the codebase are untested, or hard to test through their current interface?

Apply the **deletion test** to anything you suspect is shallow: would deleting it concentrate complexity, or just move it? A "yes, concentrates" is the signal you want.

### Emit candidate cards

Report each candidate as a card, in plain text (no HTML, no diagrams, no file written). Use `CONTEXT.md` vocabulary for the domain and the `/codebase-design` vocabulary for the architecture: if `CONTEXT.md` defines "Order," talk about "the Order intake module" — not "the FooBarHandler," and not "the Order service."

Each card carries exactly these fields:

- **Files** — which files/modules are involved.
- **Problem** — why the current architecture causes friction, in glossary terms.
- **Solution** — plain-English description of what would change.
- **Benefits** — explained in terms of locality and leverage, and how tests would improve.
- **Recommendation strength** — one of `Strong`, `Worth exploring`, `Speculative`.

**ADR conflicts**: if a candidate contradicts an existing ADR, only surface it when the friction is real enough to warrant revisiting the ADR. Mark it clearly in the card (e.g. _"contradicts ADR-0007 — but worth reopening because…"_). Don't list every theoretical refactor an ADR forbids.

Do NOT propose concrete interfaces, do NOT rank into a single top pick, and do NOT ask which to explore — a human triages the cards later.

If Explore produced no candidates, say so plainly and emit no cards.

## Rules

- **Read-only on the repo**: no edits, no commits, no files written into it — including no HTML report.
- **No interaction**: ask no questions, run no grilling loop, enter no interactive mode.
- **One pass**: explore once and emit; never loop or re-run.
- **Vocabulary is load-bearing**: use the `/codebase-design` glossary terms exactly; reach for an existing term before inventing one.
