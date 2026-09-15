<script setup>
import { computed, ref } from "vue";
import { runView } from "../models/runView.js";

const props = defineProps({
  run: { type: Object, required: true },
  now: { type: Number, required: true },
  deletable: { type: Boolean, default: false },
});

defineEmits(["delete"]);

const view = computed(() => runView(props.run, props.now));
const expanded = ref(false);
const toggleFill = computed(() => {
  const pct = view.value.devLoopProgress?.pct ?? 0;
  return `linear-gradient(to right, rgba(88, 166, 255, 0.25) ${pct}%, var(--surface-2) ${pct}%)`;
});

function toggleDetail() {
  expanded.value = !expanded.value;
}

function detailTitle(step) {
  const name = step.showIteration ? `${step.agent} #${step.iteration}` : step.agent;
  return `${name} — ${step.model} — ${step.stats}`;
}

function groupTitle(group) {
  return `${group.category} — ${group.model} — ${group.totals.tokensLabel} tokens · ${group.totals.durationLabel}`;
}
const showGroups = computed(
  () =>
    view.value.hasSteps &&
    view.value.devGroups.filter((group) => group.steps.length > 0).length > 1,
);
function isLastRow(groupIndex, stepIndex) {
  const groups = view.value.devGroups;
  for (let scanIndex = groups.length - 1; scanIndex >= 0; scanIndex--) {
    const count = groups[scanIndex].steps.length;
    if (count === 0) continue;
    return scanIndex === groupIndex && stepIndex === count - 1;
  }
  return true;
}
</script>

<template>
  <article class="run-card" :class="view.statusClass">
    <div class="card-header">
      <div class="status-badge" :class="view.statusClass">
        <span class="status-indicator"></span>
        {{ view.statusText }}
      </div>
      <div class="repo-branch">
        <span class="repo">{{ view.repo }}</span>
        <span class="sep">/</span>
        <span class="branch mono">{{ view.branch }}</span>
      </div>
      <button
        v-if="deletable"
        type="button"
        class="delete-run"
        title="Delete this run"
        aria-label="Delete this run"
        @click="$emit('delete', run.runId)"
      >
        ×
      </button>
    </div>

    <div class="card-body">
      <div class="primary-stats">
        <div class="stat">
          <span class="stat-label">{{ view.timeLabel }}</span>
          <span class="stat-value mono" :class="view.freshnessClass">{{
            view.completed ?? view.lastSeen
          }}</span>
        </div>
        <div class="stat">
          <span class="stat-label">Started</span>
          <span class="stat-value mono">{{ view.started }}</span>
        </div>
        <div v-if="view.showVm" class="stat">
          <span class="stat-label">VM</span>
          <span class="stat-value mono">{{ view.vmName }}</span>
        </div>
      </div>

      <div class="secondary">
        <div v-if="view.showPr" class="secondary-row">
          <span class="secondary-label">PR</span>
          <a :href="view.prUrl" target="_blank" rel="noopener" class="pr-link mono">{{
            view.prUrl
          }}</a>
        </div>
        <div v-if="view.showFailure" class="secondary-row failure-row">
          <span class="secondary-label">Failure</span>
          <span class="failure-reason mono">{{ view.failureReason }}</span>
        </div>
        <div v-if="view.showFreeze" class="secondary-row">
          <span class="secondary-label">Freeze</span>
          <span class="freeze-path mono" title="Local snapshot path">{{ view.freezePath }}</span>
        </div>
      </div>
    </div>

    <div v-if="view.hasDevLoop" class="dev-loop-section">
      <button
        type="button"
        class="dev-loop-toggle mono"
        :aria-expanded="expanded ? 'true' : 'false'"
        :style="{ background: toggleFill }"
        @click="toggleDetail"
      >
        {{ expanded ? "▾ hide dev-loop detail" : "▸ show dev-loop detail" }} ·
        {{ view.devLoop.length }} steps · {{ view.devLoopTotals.tokensLabel }} tokens ·
        {{ view.devLoopTotals.durationLabel }}
      </button>
      <ol v-if="expanded" class="dev-loop" aria-label="Dev-loop detail">
        <template v-if="showGroups">
          <template
            v-for="(group, groupIndex) in view.devGroups"
            :key="`group-${group.category}-${groupIndex}`"
          >
            <li v-if="group.steps.length > 0" class="dev-loop-group">
              <span class="dev-loop-rail" aria-hidden="true">
                <span class="dev-loop-dot" :class="group.statusClass"></span>
              </span>
              <div
                class="dev-loop-group-chip"
                :class="group.statusClass"
                :title="groupTitle(group)"
              >
                <span class="dev-loop-group-name mono">{{ group.category }}</span>
                <span class="dev-loop-model mono">{{ group.model }}</span>
                <span class="dev-loop-stats mono"
                  >{{ group.totals.tokensLabel }} · {{ group.totals.durationLabel }}</span
                >
              </div>
            </li>
            <li
              v-for="(step, stepIndex) in group.steps"
              :key="`${step.agent}-${step.iteration}-${groupIndex}-${stepIndex}`"
              class="dev-loop-item"
              :class="{ 'dev-loop-is-loop': step.isLoop }"
            >
              <span class="dev-loop-rail" aria-hidden="true">
                <span class="dev-loop-dot" :class="step.statusClass"></span>
                <span v-if="!isLastRow(groupIndex, stepIndex)" class="dev-loop-arrow">↓</span></span
              >
              <div
                class="dev-loop-chip"
                :class="[step.statusClass, { loop: step.isLoop }]"
                :title="detailTitle(step)"
              >
                <span class="dev-loop-agent mono">{{
                  step.isLoop ? `↺ ${step.agent}` : step.agent
                }}</span>
                <span v-if="step.showIteration" class="dev-loop-iter mono"
                  >#{{ step.iteration }}</span
                >
                <span class="dev-loop-model mono">{{ step.model }}</span>
                <span class="dev-loop-stats mono">{{ step.stats }}</span>
              </div>
            </li>
          </template>
        </template>
        <template v-else>
          <li
            v-for="(step, index) in view.devLoop"
            :key="`${step.agent}-${step.iteration}-${index}`"
            class="dev-loop-item"
            :class="{ 'dev-loop-is-loop': step.isLoop }"
          >
            <span class="dev-loop-rail" aria-hidden="true">
              <span class="dev-loop-dot" :class="step.statusClass"></span>
              <span v-if="index < view.devLoop.length - 1" class="dev-loop-arrow">↓</span></span
            >
            <div
              class="dev-loop-chip"
              :class="[step.statusClass, { loop: step.isLoop }]"
              :title="detailTitle(step)"
            >
              <span class="dev-loop-agent mono">{{
                step.isLoop ? `↺ ${step.agent}` : step.agent
              }}</span>
              <span v-if="step.showIteration" class="dev-loop-iter mono"
                >#{{ step.iteration }}</span
              >
              <span class="dev-loop-model mono">{{ step.model }}</span>
              <span class="dev-loop-stats mono">{{ step.stats }}</span>
            </div>
          </li>
        </template>
      </ol>
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
  align-items: center;
  gap: 0.75rem;
  padding: 1rem 1.25rem;
  background: var(--surface-2);
  border-bottom: 1px solid var(--border);
}
.repo-branch {
  display: flex;
  flex: 1;
  min-width: 0;
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
.delete-run:hover,
.delete-run:focus-visible {
  color: var(--red);
  background: rgba(248, 81, 73, 0.1);
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
.dev-loop-section {
  border-top: 1px solid var(--border);
  padding: 0.75rem 1.25rem 1.25rem;
}
.dev-loop-toggle {
  width: 100%;
  text-align: left;
  background: var(--surface-2);
  color: var(--text);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: 0.5rem 0.75rem;
  cursor: pointer;
  font-size: 0.85rem;
}
.dev-loop {
  list-style: none;
  margin: 0.75rem 0 0;
  padding: 0;
}
.dev-loop-item {
  display: flex;
  gap: 0.5rem;
}
.dev-loop-group {
  display: flex;
  gap: 0.5rem;
}
.dev-loop-group-chip {
  display: inline-flex;
  align-items: baseline;
  gap: 0.5rem;
  width: fit-content;
  max-width: 100%;
  padding: 0.35rem 0.6rem;
  background: var(--surface);
  border: 1px solid var(--border);
  border-left: 3px solid var(--st, var(--text-dim));
  border-radius: var(--radius);
  margin-bottom: 0.15rem;
}
.dev-loop-group-name {
  font-weight: 700;
  font-size: 0.85rem;
  white-space: nowrap;
}
.dev-loop-is-loop {
  margin-left: 1.25rem;
}
.dev-loop-rail {
  display: flex;
  flex-direction: column;
  align-items: center;
  width: 1rem;
  flex-shrink: 0;
}
.dev-loop-dot {
  width: 0.55rem;
  height: 0.55rem;
  border-radius: 50%;
  background: var(--text-dim);
  flex-shrink: 0;
  margin-top: 0.45rem;
}
.dev-loop-dot.stage-done {
  background: var(--blue);
}
.dev-loop-dot.stage-running {
  background: var(--green);
  animation: blink 1.4s infinite;
}
.dev-loop-dot.stage-pending {
  background: var(--text-dim);
}
.dev-loop-dot.stage-failed {
  background: var(--red);
}
.dev-loop-arrow {
  color: var(--text-dim);
  font-size: 0.8rem;
  line-height: 1.6;
}
.dev-loop-chip {
  display: inline-flex;
  align-items: baseline;
  gap: 0.5rem;
  width: fit-content;
  max-width: 100%;
  padding: 0.35rem 0.6rem;
  background: var(--surface-2);
  border: 1px solid var(--border);
  border-left: 3px solid var(--st, var(--text-dim));
  border-radius: var(--radius);
  margin-bottom: 0.15rem;
}
.dev-loop-chip.stage-done,
.dev-loop-group-chip.stage-done {
  --st: var(--blue);
}
.dev-loop-chip.stage-running,
.dev-loop-group-chip.stage-running {
  --st: var(--green);
}
.dev-loop-chip.stage-pending,
.dev-loop-group-chip.stage-pending {
  --st: var(--text-dim);
}
.dev-loop-chip.stage-failed,
.dev-loop-group-chip.stage-failed {
  --st: var(--red);
}
.dev-loop-chip.loop {
  border-style: dashed;
  border-color: var(--st, var(--text-dim));
}
.dev-loop-chip.stage-pending .dev-loop-agent {
  color: var(--text-muted);
}
.dev-loop-agent {
  font-weight: 600;
  font-size: 0.85rem;
  white-space: nowrap;
}
.dev-loop-iter {
  font-size: 0.72rem;
  color: var(--text-muted);
  border: 1px solid var(--border);
  border-radius: 999px;
  padding: 0 0.4rem;
  white-space: nowrap;
}
.dev-loop-model {
  color: var(--cyan);
  font-size: 0.76rem;
  white-space: nowrap;
}
.dev-loop-stats {
  margin-left: auto;
  color: var(--text-muted);
  font-size: 0.78rem;
  white-space: nowrap;
}
.mono {
  font-family: var(--font-mono);
}
</style>
