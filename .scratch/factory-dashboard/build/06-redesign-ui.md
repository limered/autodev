# 06 — Redesign the dashboard UI (modern with a retro touch)

**What to build:** Restyle the existing Factory Dashboard single-page Vue app (`dashboard/src/web`) so it looks polished and intentional instead of a bare HTML table. Keep all current data and behaviour identical — this is a visual/UX redesign only, no backend or API changes. The aesthetic is **modern with a retro touch**: contemporary layout, spacing and typography, but with a nod to retro computing/terminal styling in the colour palette and accents.

This ticket is implemented by the autodev feature-builder agent in a disposable VM. It must not change the API contract or the polling behaviour.

## Context

The app is a Vue 3 + Vite SPA in `dashboard/src/web`. It polls `GET /runs` every 5 seconds and renders one row per job run. It is served same-origin by the .NET backend; do not introduce a separate API base URL or CORS.

Each run object has these fields (JSON, camelCase):

- `runId` (guid), `repo` (owner/name), `branch`, `spec`, `model`
- `vmName` (nullable)
- `status` — one of: `launching`, `running`, `stalled`, `done`, `failed`
- `startedAt`, `finishedAt` (nullable), `lastHeartbeatAt` (nullable)
- `prUrl` (nullable), `failureReason` (nullable)
- `freezeCaptured` (bool), `freezeLocalPath` (nullable — a host-local path, NOT a clickable link)
- `updatedAt`

The current UI computes a live "last seen Ns ago" string from `lastHeartbeatAt`, ticking every second. Preserve this.

## Design direction

- **Modern with a retro touch.** Modern: clean layout, generous spacing, readable type hierarchy, responsive to a laptop screen. Retro touch: a dark terminal-inspired theme, a tasteful accent palette (think phosphor green / amber / cyan CRT vibes) used sparingly for status and highlights, and a monospace or mono-adjacent font for machine data (ids, paths, branches). Avoid skeuomorphic gimmicks (no scanline overlays so heavy it hurts readability) — restraint over kitsch.
- **Status is the star.** Each of the five statuses should be instantly distinguishable at a glance via colour and/or a small badge/indicator: `launching`, `running` (feels alive — subtle pulse/animation acceptable), `stalled` (warning), `done` (success), `failed` (error). Pick a coherent colour for each and apply consistently.
- **Liveness.** The "last seen" freshness should read clearly and can visually flag staleness (e.g. dim or warn when a running job hasn't been seen in a while).
- **Layout.** Cards or a well-styled table are both fine — choose what reads best. Show the key fields prominently (repo/branch, model, status, last seen) and the secondary ones (PR link, failure reason, freeze path) without clutter. The PR should be a clear link; the failure reason visible on failed runs; the freeze path shown as monospace text (it is not a link).
- **Empty and error states** should look designed, not default (e.g. a tasteful "No runs yet" and a clear error banner when the fetch fails).

## Constraints

- Visual/UX only. Do NOT change: the API endpoints, the 5s polling, the field contract, or the .NET/serving setup.
- Stay within the existing Vue 3 + Vite app. A single lightweight styling approach is fine (scoped CSS, CSS variables for the palette). Do not add a heavy UI framework or new build tooling unless it's a small, well-justified dependency — prefer plain CSS.
- `npm run build` must succeed and produce `dist/` (the Dockerfile builds it).

**Blocked by:** None — the API and data contract are already live (tickets 02–05).

**Status:** ready-for-agent

- [ ] The dashboard has a cohesive "modern with a retro touch" visual theme (dark, terminal-inspired, restrained CRT-accent palette, mono font for machine data).
- [ ] All five statuses are instantly distinguishable via consistent colour/badge treatment; `running` reads as alive.
- [ ] The live "last seen Ns ago" indicator is preserved and clearly styled, with visible staleness cues.
- [ ] Repo/branch, model, status, last seen are prominent; PR link, failure reason, and freeze path (monospace, not a link) are shown without clutter.
- [ ] Empty ("no runs yet") and fetch-error states are intentionally styled.
- [ ] No API/contract/polling changes; `npm run build` succeeds.
