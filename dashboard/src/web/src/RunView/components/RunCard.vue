<script setup>
import { computed } from "vue";
import { runView } from "../models/runView.js";

// A single run card, shared by the active list and the history list (issue #52
// split). Owns the run → view mapping so list components stay thin; `now` is a
// prop because the freshness ticker belongs to the list (one timer per list,
// not per card).
const props = defineProps({
  run: { type: Object, required: true },
  now: { type: Number, required: true },
  // Active-runs list opts in to the delete control; history runs are terminal
  // and left read-only.
  deletable: { type: Boolean, default: false },
});

defineEmits(["delete"]);

const view = computed(() => runView(props.run, props.now));
</script>

<template>
  <article class="run-card" :class="view.statusClass">
    <div class="card-header">
      <div class="identity">
        <div class="repo-branch">
          <span class="repo">{{ run.repo }}</span>
          <span class="sep">/</span>
          <span class="branch mono">{{ run.branch }}</span>
        </div>
        <ul v-if="view.stages.length" class="stage-strip">
          <li
            v-for="stage in view.stages"
            :key="stage.agent"
            class="stage-badge"
            :class="stage.statusClass"
          >
            <span class="stage-head">
              <span class="stage-indicator"></span>
              <span class="stage-agent">{{ stage.agent }}</span>
            </span>
            <span class="stage-model mono">{{ stage.model }}</span>
          </li>
        </ul>
      </div>
      <div class="status-badge" :class="view.statusClass">
        <span class="status-indicator"></span>
        {{ run.status }}
      </div>
      <button
        v-if="deletable"
        type="button"
        class="delete-run"
        title="Delete this run"
        @click="$emit('delete', run.runId)"
      >
        Delete
      </button>
    </div>

    <div class="card-body">
      <div class="primary-stats">
        <div class="stat">
          <!-- Terminal runs show a fixed Completed timestamp (runView leaves
               lastSeen null); active runs show the live Last seen label. -->
          <span class="stat-label">{{ view.timeLabel }}</span>
          <span class="stat-value mono" :class="view.freshnessClass">{{
            view.completed ?? view.lastSeen
          }}</span>
        </div>
        <div class="stat">
          <span class="stat-label">Started</span>
          <span class="stat-value mono">{{ view.started }}</span>
        </div>
        <div v-if="run.vmName" class="stat">
          <span class="stat-label">VM</span>
          <span class="stat-value mono">{{ run.vmName }}</span>
        </div>
      </div>

      <div class="secondary">
        <div v-if="run.prUrl" class="secondary-row">
          <span class="secondary-label">PR</span>
          <a :href="run.prUrl" target="_blank" rel="noopener" class="pr-link mono">{{
            run.prUrl
          }}</a>
        </div>
        <div v-if="run.status === 'failed' && run.failureReason" class="secondary-row failure-row">
          <span class="secondary-label">Failure</span>
          <span class="failure-reason mono">{{ run.failureReason }}</span>
        </div>
        <div v-if="run.freezeCaptured" class="secondary-row">
          <span class="secondary-label">Freeze</span>
          <span class="freeze-path mono" title="Local snapshot path">{{
            run.freezeLocalPath || "—"
          }}</span>
        </div>
      </div>
    </div>
  </article>
</template>

<style scoped>
.run-card {
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  box-shadow: var(--shadow);
  overflow: hidden;
  transition: border-color 0.2s ease;
}
.run-card:hover {
  border-color: var(--text-dim);
}
.card-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  padding: 1rem 1.25rem;
  background: var(--surface-2);
  border-bottom: 1px solid var(--border);
}
.identity {
  min-width: 0;
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
  margin-top: 0.4rem;
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
.delete-run {
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
.delete-run:hover {
  background: var(--red);
  color: var(--surface);
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
.card-body {
  padding: 1rem 1.25rem 1.25rem;
}
.primary-stats {
  display: flex;
  flex-wrap: wrap;
  gap: 1.25rem 2rem;
  margin-bottom: 1rem;
}
.stat {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}
.stat-label {
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--text-dim);
}
.stat-value {
  font-size: 0.95rem;
  color: var(--text);
}
.stat-value.fresh {
  color: var(--green);
}
.stat-value.stale-warn {
  color: var(--amber);
}
.stat-value.stale-danger {
  color: var(--red);
}
.stat-value.settled {
  color: var(--text-muted);
}
.stat-value.unknown {
  color: var(--text-dim);
}
.secondary {
  display: grid;
  gap: 0.5rem;
  padding-top: 1rem;
  border-top: 1px solid var(--border);
}
.secondary-row {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  font-size: 0.9rem;
}
.secondary-label {
  flex-shrink: 0;
  width: 4rem;
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--text-dim);
  padding-top: 0.15rem;
}
.pr-link {
  color: var(--blue);
  text-decoration: none;
  word-break: break-all;
}
.pr-link:hover {
  text-decoration: underline;
}
.failure-row .failure-reason {
  color: var(--red);
  word-break: break-word;
}
.freeze-path {
  color: var(--text-muted);
  word-break: break-all;
}
.mono {
  font-family: var(--font-mono);
}
</style>
