<script setup>
import { computed } from "vue";
import { useRoute } from "vue-router";
import { runView } from "../../RunView/models/runView.js";

// Throwaway prototype for issue #99: three candidate layouts for the RunCard
// header, side by side against sample runs, switched via ?variant=A|B|C.
// Not production code — this lives only on the prototype/runcard-header
// branch and is deleted once a variant is folded into RunCard.vue.
const route = useRoute();

const ORDER = ["A", "B", "C"];
const variant = computed(() => {
  const v = String(route.query.variant ?? "C").toUpperCase();
  return ORDER.includes(v) ? v : "C";
});

const NOTES = {
  A: "one flex row; pills stacked in a column at the end — smallest change, strip still shares the row",
  B: "meta line on top; strip indented underneath inside the same header block — pills fixed, strip still cramped",
  C: "meta line on top; stage strip as a full-width band below the header divider — wraps freely",
};

// Fixed clock: the header prototype only needs the status/stage classes, and
// a frozen `now` keeps renders deterministic while flipping variants.
const NOW = new Date("2026-09-04T12:00:00Z").getTime();

const stage = (agent, status) => ({ agent, model: "glm-5.2", status });

// Sample headers chosen to expose the failure mode: the middle one carries
// enough stages to force the strip to wrap, which is exactly what disturbs
// the pills in the layouts that share a flex row with them.
const samples = [
  {
    label: "few stages",
    repo: "limered/autodev",
    branch: "factory/header-rework",
    status: "running",
    startedAt: "2026-09-04T11:00:00Z",
    lastHeartbeatAt: "2026-09-04T11:59:50Z",
    deletable: true,
    stages: [stage("triage", "done"), stage("feature-builder", "running")],
  },
  {
    label: "many stages (strip wraps)",
    repo: "limered/autodev",
    branch: "main",
    status: "running",
    startedAt: "2026-09-04T10:00:00Z",
    lastHeartbeatAt: "2026-09-04T11:58:20Z",
    deletable: true,
    stages: [
      stage("triage", "done"),
      stage("feature-builder", "done"),
      stage("test-runner", "running"),
      stage("static-analysis", "pending"),
      stage("fix-findings", "pending"),
      stage("agentic-review", "pending"),
      stage("pr-author", "pending"),
      stage("cleanup", "pending"),
    ],
  },
  {
    label: "no stages",
    repo: "limered/autodev",
    branch: "issue-99",
    status: "failed",
    startedAt: "2026-09-04T09:00:00Z",
    lastHeartbeatAt: "2026-09-04T09:40:00Z",
    deletable: true,
    stages: [],
  },
];

const views = computed(() => samples.map((s) => ({ sample: s, view: runView(s, NOW) })));
</script>

<template>
  <section class="proto-page">
    <h1>RunCard header prototype</h1>

    <nav class="variant-switch mono" aria-label="Prototype variant">
      <RouterLink
        v-for="v in ORDER"
        :key="v"
        class="switch-link"
        :class="{ active: variant === v }"
        :to="{ query: { variant: v } }"
      >
        Variant {{ v }}
      </RouterLink>
    </nav>
    <p class="variant-note mono">variant {{ variant }}: {{ NOTES[variant] }}</p>

    <div v-for="{ sample, view } in views" :key="sample.label" class="proto-sample">
      <p class="sample-label mono">{{ sample.label }}</p>

      <!-- Variant A: today's single flex row, with the two pills stacked in a
           column at the end so they stop competing for horizontal space. The
           stage strip still lives inside .identity, so wrapping stages still
           grow the row the pills sit in — just less violently. -->
      <article v-if="variant === 'A'" class="proto-card" :class="view.statusClass">
        <div class="hdr-a">
          <div class="identity-a">
            <div class="repo-branch">
              <span class="repo">{{ sample.repo }}</span>
              <span class="sep">/</span>
              <span class="branch mono">{{ sample.branch }}</span>
            </div>
            <ul v-if="view.stages.length" class="stage-strip">
              <li
                v-for="stageView in view.stages"
                :key="stageView.agent"
                class="stage-badge"
                :class="stageView.statusClass"
              >
                <span class="stage-head">
                  <span class="stage-indicator"></span>
                  <span class="stage-agent">{{ stageView.agent }}</span>
                </span>
                <span class="stage-model mono">{{ stageView.model }}</span>
              </li>
            </ul>
          </div>
          <div class="pills-a">
            <div class="status-badge" :class="view.statusClass">
              <span class="status-indicator"></span>
              {{ sample.status }}
            </div>
            <button type="button" class="delete-run bordered">Delete</button>
          </div>
        </div>
      </article>

      <!-- Variant B: pills leave the identity row — a meta line (pill ·
           repo/branch · abort) sits on top and the strip moves to a second
           line, but it stays indented inside the same header block with no
           divider, so the strip reads as a continuation of the identity
           rather than its own band. -->
      <article v-if="variant === 'B'" class="proto-card" :class="view.statusClass">
        <div class="hdr-b">
          <div class="meta-b">
            <div class="status-badge" :class="view.statusClass">
              <span class="status-indicator"></span>
              {{ sample.status }}
            </div>
            <div class="repo-branch">
              <span class="repo">{{ sample.repo }}</span>
              <span class="sep">/</span>
              <span class="branch mono">{{ sample.branch }}</span>
            </div>
            <button type="button" class="delete-run bordered">Delete</button>
          </div>
          <ul v-if="view.stages.length" class="stage-strip indent-b">
            <li
              v-for="stageView in view.stages"
              :key="stageView.agent"
              class="stage-badge"
              :class="stageView.statusClass"
            >
              <span class="stage-head">
                <span class="stage-indicator"></span>
                <span class="stage-agent">{{ stageView.agent }}</span>
              </span>
              <span class="stage-model mono">{{ stageView.model }}</span>
            </li>
          </ul>
        </div>
      </article>

      <!-- Variant C: the meta line is the whole header (pill fixed at the
           start, repo/branch flexing to fill, abort pinned at the end), and
           the stage strip becomes a separate full-width band below the header
           divider — its own row, free to wrap without moving the pills. The
           abort control turns into a borderless × that only tints red on
           hover. -->
      <article v-if="variant === 'C'" class="proto-card" :class="view.statusClass">
        <div class="hdr-c">
          <div class="status-badge" :class="view.statusClass">
            <span class="status-indicator"></span>
            {{ sample.status }}
          </div>
          <div class="repo-branch">
            <span class="repo">{{ sample.repo }}</span>
            <span class="sep">/</span>
            <span class="branch mono">{{ sample.branch }}</span>
          </div>
          <button
            type="button"
            class="delete-run x"
            title="Delete this run"
            aria-label="Delete this run"
          >
            ×
          </button>
        </div>
        <ul v-if="view.stages.length" class="stage-strip band-c">
          <li
            v-for="stageView in view.stages"
            :key="stageView.agent"
            class="stage-badge"
            :class="stageView.statusClass"
          >
            <span class="stage-head">
              <span class="stage-indicator"></span>
              <span class="stage-agent">{{ stageView.agent }}</span>
            </span>
            <span class="stage-model mono">{{ stageView.model }}</span>
          </li>
        </ul>
      </article>
    </div>
  </section>
</template>

<style scoped>
.proto-page {
  display: grid;
  gap: 1.5rem;
}
h1 {
  margin: 0;
  font-size: 1.3rem;
}
.variant-switch {
  display: inline-flex;
  gap: 0.5rem;
}
.switch-link {
  padding: 0.35rem 0.8rem;
  color: var(--text-muted);
  text-decoration: none;
  border: 1px solid var(--border);
  border-radius: var(--radius);
}
.switch-link.active {
  color: var(--bg);
  background: var(--accent);
  border-color: var(--accent);
}
.variant-note {
  margin: -1rem 0 0;
  color: var(--text-dim);
  font-size: 0.8rem;
}
.proto-sample {
  display: grid;
  gap: 0.5rem;
}
.sample-label {
  margin: 0;
  color: var(--text-dim);
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.08em;
}

/* Shared card/header skin, mirroring RunCard.vue closely enough that the
   layouts read the way they would in production. */
.proto-card {
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  box-shadow: var(--shadow);
  overflow: hidden;
}
.repo-branch {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 0.35rem;
  font-size: 1.1rem;
  font-weight: 600;
}
.repo {
  color: var(--text);
}
.sep {
  color: var(--text-dim);
}
.branch {
  color: var(--cyan);
}
.stage-strip {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
  margin: 0;
  padding: 0;
  list-style: none;
}
.stage-badge {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  padding: 0.3rem 0.55rem;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  line-height: 1.2;
}
.stage-head {
  display: flex;
  align-items: center;
  gap: 0.3rem;
}
.stage-indicator {
  width: 0.45rem;
  height: 0.45rem;
  border-radius: 50%;
  background: var(--text-dim);
  flex-shrink: 0;
}
.stage-agent {
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--text);
}
.stage-model {
  font-size: 0.7rem;
  color: var(--text-muted);
}
.stage-pending .stage-indicator {
  background: var(--text-dim);
}
.stage-pending .stage-agent {
  color: var(--text-muted);
}
.stage-running {
  border-color: var(--green);
}
.stage-running .stage-indicator {
  background: var(--green);
  animation: blink 1.4s infinite;
}
.stage-running .stage-agent {
  color: var(--green);
}
.stage-done {
  border-color: var(--blue);
}
.stage-done .stage-indicator {
  background: var(--blue);
}
.stage-done .stage-agent {
  color: var(--blue);
}
.status-badge {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  flex-shrink: 0;
  padding: 0.35rem 0.7rem;
  border-radius: 999px;
  font-size: 0.8rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  background: var(--surface);
  border: 1px solid currentColor;
}
.status-indicator {
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 50%;
  background: currentColor;
}
.status-launching {
  color: var(--cyan);
}
.status-running {
  color: var(--green);
}
.status-running .status-indicator {
  animation: blink 1.4s infinite;
}
.status-stalled {
  color: var(--amber);
}
.status-done {
  color: var(--blue);
}
.status-failed {
  color: var(--red);
}
@keyframes blink {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.35;
  }
}
.mono {
  font-family: var(--font-mono);
}

/* Variant A: one row, pills stacked at the end. */
.hdr-a {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  padding: 1rem 1.25rem;
  background: var(--surface-2);
  border-bottom: 1px solid var(--border);
}
.identity-a {
  min-width: 0;
}
.identity-a .stage-strip {
  margin-top: 0.4rem;
}
.pills-a {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.5rem;
  flex-shrink: 0;
}

/* Variant B: meta line on top, strip indented inside the same block. */
.hdr-b {
  padding: 1rem 1.25rem;
  background: var(--surface-2);
  border-bottom: 1px solid var(--border);
}
.meta-b {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}
.meta-b .repo-branch {
  flex: 1;
  min-width: 0;
}
.indent-b {
  margin-top: 0.6rem;
}

/* Variant C: meta line is the header; strip is a full-width band below the
   header divider. */
.hdr-c {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 1rem 1.25rem;
  background: var(--surface-2);
  border-bottom: 1px solid var(--border);
}
.hdr-c .repo-branch {
  flex: 1;
  min-width: 0;
}
.band-c {
  padding: 0.6rem 1.25rem;
  background: var(--surface-2);
  border-bottom: 1px solid var(--border);
}

/* The abort control: bordered "Delete" in A/B (today's look), borderless ×
   with a red hover tint in C. */
.delete-run.bordered {
  flex-shrink: 0;
  align-self: flex-start;
  padding: 0.3rem 0.7rem;
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--red);
  background: var(--surface);
  border: 1px solid var(--red);
  border-radius: var(--radius);
  cursor: pointer;
}
.delete-run.bordered:hover {
  background: var(--red);
  color: var(--surface);
}
.delete-run.x {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: 1.75rem;
  height: 1.75rem;
  padding: 0;
  font-size: 1.25rem;
  font-weight: 600;
  line-height: 1;
  color: var(--text-muted);
  background: transparent;
  border: none;
  border-radius: var(--radius);
  cursor: pointer;
}
.delete-run.x:hover,
.delete-run.x:focus-visible {
  color: var(--red);
  background: rgba(248, 81, 73, 0.1);
}
</style>
