<script setup>
import { computed } from "vue";
import { useRoute } from "vue-router";
import { usePollingFeed } from "../../_shared/services/usePollingFeed.js";
import { hostView } from "../models/hostView.js";
import EligibleIssuesColumn from "./EligibleIssuesColumn.vue";
import RunQueueColumn from "./RunQueueColumn.vue";
import WorkflowPickerPrototypePanel from "./prototype/WorkflowPickerPrototypePanel.vue";

// Read composition layer for the dispatch split: owns the three feeds and
// the cross-feed wiring — the header indicators and the queued-issue ids the
// eligible column filters by. Each column owns its own writes and re-syncs
// (wired through the shared useQueueHandlers composable, Block A), so only
// read state and the feed loads flow down: no action state or handler passes
// through. A write in flight surfaces where it belongs — the queue column's
// "saving…" chip and the eligible column's disabled enqueue buttons.
const issuesFeed = usePollingFeed(() => fetch("/issues"));
const queueFeed = usePollingFeed(() => fetch("/queue"));
const hostFeed = usePollingFeed(() => fetch("/host"));

const { items: issues } = issuesFeed;
const { items: queue } = queueFeed;
const { items: host } = hostFeed;

// The eligible column's filter input: which synced issues are already queued.
const queuedIssueIds = computed(() => new Set(queue.value.map((q) => q.issueId)));

const connectionError = computed(() => issuesFeed.error.value || queueFeed.error.value);
const isPollingLoading = computed(() => issuesFeed.isLoading.value || queueFeed.isLoading.value);
const hostBadge = computed(() => hostView(host.value, Date.now()));
const route = (() => {
  try {
    return useRoute() ?? { query: {} };
  } catch {
    return { query: {} };
  }
})();
const showPrototype = computed(() => (route?.query?.variant ?? undefined) !== undefined);
const isProd = import.meta.env.PROD;
</script>

<template>
  <section class="dispatch-panel">
    <header class="dispatch-header">
      <h2><span class="prompt">&gt;</span> Dispatch</h2>
      <div class="dispatch-indicators">
        <div class="connection" :class="{ loading: isPollingLoading }">
          <span class="connection-dot"></span>
          <span v-if="connectionError">sync error</span>
          <span v-else>live</span>
        </div>
        <div class="connection host-badge" :class="hostBadge.freshnessClass">
          <span class="connection-dot"></span>
          <span>{{ hostBadge.label }}</span>
          <span v-if="!hostBadge.online" class="host-last-seen"
            >(last seen {{ hostBadge.lastSeen }})</span
          >
        </div>
      </div>
    </header>

    <div class="dispatch-columns">
      <EligibleIssuesColumn
        :issues="issues"
        :queued-issue-ids="queuedIssueIds"
        :sync-error="issuesFeed.error.value"
        :reload-issues="issuesFeed.load"
        :reload-queue="queueFeed.load"
      />

      <RunQueueColumn
        :queue="queue"
        :sync-error="queueFeed.error.value"
        :reload-issues="issuesFeed.load"
        :reload-queue="queueFeed.load"
      />
    </div>

    <WorkflowPickerPrototypePanel v-if="showPrototype && !isProd" />
  </section>
</template>

<style scoped>
.dispatch-panel {
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.dispatch-columns {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 1.5rem;
}

@media (max-width: 900px) {
  .dispatch-columns {
    grid-template-columns: 1fr;
  }
}

.dispatch-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
  padding-bottom: 0.75rem;
  border-bottom: 1px solid var(--border);
}

h2 {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 700;
  letter-spacing: -0.01em;
}

.prompt {
  color: var(--accent);
  margin-right: 0.25rem;
}

.dispatch-indicators {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.connection {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.25rem 0.6rem;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: 999px;
  font-size: 0.7rem;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.connection.loading {
  animation: pulse 1.4s infinite;
}

.host-badge.fresh {
  color: var(--green);
  border-color: var(--green);
}

.host-badge.fresh .connection-dot {
  background: var(--green);
}

.host-badge.stale-danger,
.host-badge.unknown {
  color: var(--red);
  border-color: var(--red);
}

.host-badge.stale-danger .connection-dot,
.host-badge.unknown .connection-dot {
  background: var(--red);
}

.host-last-seen {
  text-transform: none;
  letter-spacing: 0;
}

.connection-dot {
  width: 0.4rem;
  height: 0.4rem;
  border-radius: 50%;
  background: var(--accent);
}

@keyframes pulse {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.6;
  }
}
</style>
