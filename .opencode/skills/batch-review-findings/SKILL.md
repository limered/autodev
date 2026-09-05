---
name: batch-review-findings
description: Collect open agentic-review issues from GitHub, dedupe their finding cards, and group them into work batches ready to grill into agent-workable issues. Use when the user wants to consolidate agentic-review findings, dedupe review issues, or turn review noise into work blocks.
disable-model-invocation: true
---

# Batch Review Findings

Turn the pile of open `agentic-review` issues into a small set of **work batches**. Each batch is a cluster of deduped findings the user can then grill (via `/grill-me` or `/grilling`) into real `ready-for-agent` issues.

Agentic-review issues are advisory dumps: one issue per factory job, each containing several **finding cards** (`Candidate N —` from `improve-codebase-architecture`, or findings from `code-review`). The same problem recurs across jobs, and related problems scatter across issues. This skill collapses that noise.

## Process

### 1. Fetch the findings

```
gh issue list --repo limered/autodev --label agentic-review --state open --limit 200 --json number,title,body,url
```

Each card is a unit. Split every issue body into its finding cards (cards are separated by `---` and headed `**Candidate N —` or a bold finding title). Track for each card: source issue number, title, files touched, problem, proposed solution, recommendation strength.

### 2. Dedupe

Two cards are **duplicates** when they name the same problem on the same module/seam — judge by the domain concept and the files listed, not by wording. Recurring findings across jobs are the common case.

Merge duplicates into one card. Keep the clearest problem/solution statement, union the file lists, keep the strongest recommendation strength, and record every source issue number so nothing is lost.

### 3. Group into batches

Cluster the deduped cards into **work batches** by locality — cards that touch the same module, theme folder (`DispatchView/`, `RunView/`, `_shared/`), or the same seam belong together. A batch should be grillable in one sitting and land as one or a few related issues.

Respect the frontend structure rules in `AGENTS.md` (semantic-by-theme) and the codebase-design vocabulary when naming batches.

### 4. Present

Show the batches as a numbered list. Per batch:

- **Batch title** — the module/theme and the shared concern
- **Findings** — the deduped cards in it, each with its source issue numbers (e.g. `#109, #106`)
- **Files** — the union of files touched
- **Suggested scope** — one sentence on what a grill session would turn this into

Do **not** create, close, or modify any issue. This skill only reads and organizes — output is the batch list the user grills next.

Ask the user which batch to grill first, then hand off to `/grill-me`.
