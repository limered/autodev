<script setup>
import { computed, onMounted, onUnmounted, ref } from 'vue'
import ErrorBanner from '../../_shared/components/ErrorBanner.vue'
import { runView } from '../models/runView.js'
import { usePagedRuns } from '../services/usePagedRuns.js'

const now = ref(Date.now())
let tickTimer = null

const { runs, error, hasMore, isLoading, loadNext } = usePagedRuns(url => fetch(url))

const displayedRuns = computed(() =>
  runs.value.map(r => ({ run: r, view: runView(r, now.value) }))
)

onMounted(() => { tickTimer = setInterval(() => { now.value = Date.now() }, 1000) })
onUnmounted(() => clearInterval(tickTimer))
</script>

<template>
  <div>
    <ErrorBanner
      v-if="error"
      title="Connection lost"
      :message="`Failed to load runs: ${error}`"
    />

    <section v-if="displayedRuns.length" class="run-list">
      <article
        v-for="r in displayedRuns"
        :key="r.run.runId"
        class="run-card"
        :class="r.view.statusClass"
      >
        <div class="card-header">
          <div class="identity">
            <div class="repo-branch">
              <span class="repo">{{ r.run.repo }}</span>
              <span class="sep">/</span>
              <span class="branch mono">{{ r.run.branch }}</span>
            </div>
            <ul v-if="r.view.stages.length" class="stage-strip">
              <li
                v-for="stage in r.view.stages"
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
          <div class="status-badge" :class="r.view.statusClass">
            <span class="status-indicator"></span>
            {{ r.run.status }}
          </div>
        </div>

        <div class="card-body">
          <div class="primary-stats">
            <div class="stat">
              <span class="stat-label">Last seen</span>
              <span class="stat-value mono" :class="r.view.freshnessClass">{{ r.view.lastSeen }}</span>
            </div>
            <div class="stat">
              <span class="stat-label">Started</span>
              <span class="stat-value mono">{{ r.view.started }}</span>
            </div>
            <div v-if="r.run.vmName" class="stat">
              <span class="stat-label">VM</span>
              <span class="stat-value mono">{{ r.run.vmName }}</span>
            </div>
          </div>

          <div class="secondary">
            <div v-if="r.run.prUrl" class="secondary-row">
              <span class="secondary-label">PR</span>
              <a :href="r.run.prUrl" target="_blank" rel="noopener" class="pr-link mono">{{ r.run.prUrl }}</a>
            </div>
            <div v-if="r.run.status === 'failed' && r.run.failureReason" class="secondary-row failure-row">
              <span class="secondary-label">Failure</span>
              <span class="failure-reason mono">{{ r.run.failureReason }}</span>
            </div>
            <div v-if="r.run.freezeCaptured" class="secondary-row">
              <span class="secondary-label">Freeze</span>
              <span class="freeze-path mono" title="Local snapshot path">{{ r.run.freezeLocalPath || 'â€”' }}</span>
            </div>
          </div>
        </div>
      </article>
    </section>

    <section v-else-if="!error" class="empty-state">
      <div class="empty-prompt">&gt;_</div>
      <h2>No runs yet</h2>
      <p>The factory is idle. New runs will appear here as they start.</p>
    </section>
    <div v-if="hasMore" class="load-more-row">
      <button type="button" class="load-more" :disabled="isLoading" @click="loadNext">
        {{ isLoading ? 'Loading...' : 'Load more' }}
      </button>
    </div>
  </div>
</template>

<style scoped>
.run-list { display: grid; gap: 1rem; }
.run-card { background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); box-shadow: var(--shadow); overflow: hidden; transition: border-color 0.2s ease; }
.run-card:hover { border-color: var(--text-dim); }
.card-header { display: flex; align-items: flex-start; justify-content: space-between; gap: 1rem; padding: 1rem 1.25rem; background: var(--surface-2); border-bottom: 1px solid var(--border); }
.identity { min-width: 0; }
.repo-branch { display: flex; flex-wrap: wrap; align-items: baseline; gap: 0.35rem; font-size: 1.1rem; font-weight: 600; }
.repo { color: var(--text); }
.sep { color: var(--text-dim); }
.branch { color: var(--cyan); }
.stage-strip { display: flex; flex-wrap: wrap; gap: 0.4rem; margin-top: 0.4rem; padding: 0; list-style: none; }
.stage-badge { display: flex; flex-direction: column; gap: 0.15rem; padding: 0.3rem 0.55rem; background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); line-height: 1.2; }
.stage-head { display: flex; align-items: center; gap: 0.3rem; }
.stage-indicator { width: 0.45rem; height: 0.45rem; border-radius: 50%; background: var(--text-dim); flex-shrink: 0; }
.stage-agent { font-size: 0.78rem; font-weight: 600; color: var(--text); }
.stage-model { font-size: 0.7rem; color: var(--text-muted); }
.stage-pending .stage-indicator { background: var(--text-dim); }
.stage-pending .stage-agent { color: var(--text-muted); }
.stage-running { border-color: var(--green); }
.stage-running .stage-indicator { background: var(--green); animation: blink 1.4s infinite; }
.stage-running .stage-agent { color: var(--green); }
.stage-done { border-color: var(--blue); }
.stage-done .stage-indicator { background: var(--blue); }
.stage-done .stage-agent { color: var(--blue); }
.status-badge { display: inline-flex; align-items: center; gap: 0.4rem; flex-shrink: 0; padding: 0.35rem 0.7rem; border-radius: 999px; font-size: 0.8rem; font-weight: 600; text-transform: uppercase; letter-spacing: 0.04em; background: var(--surface); border: 1px solid currentColor; }
.status-indicator { width: 0.5rem; height: 0.5rem; border-radius: 50%; background: currentColor; }
.status-launching { color: var(--cyan); }
.status-running { color: var(--green); }
.status-running .status-indicator { animation: blink 1.4s infinite; }
.status-stalled { color: var(--amber); }
.status-done { color: var(--blue); }
.status-failed { color: var(--red); }
@keyframes blink { 0%, 100% { opacity: 1; } 50% { opacity: 0.35; } }
.card-body { padding: 1rem 1.25rem 1.25rem; }
.primary-stats { display: flex; flex-wrap: wrap; gap: 1.25rem 2rem; margin-bottom: 1rem; }
.stat { display: flex; flex-direction: column; gap: 0.15rem; }
.stat-label { font-size: 0.7rem; font-weight: 600; text-transform: uppercase; letter-spacing: 0.08em; color: var(--text-dim); }
.stat-value { font-size: 0.95rem; color: var(--text); }
.stat-value.fresh { color: var(--green); }
.stat-value.stale-warn { color: var(--amber); }
.stat-value.stale-danger { color: var(--red); }
.stat-value.settled { color: var(--text-muted); }
.stat-value.unknown { color: var(--text-dim); }
.secondary { display: grid; gap: 0.5rem; padding-top: 1rem; border-top: 1px solid var(--border); }
.secondary-row { display: flex; align-items: flex-start; gap: 0.75rem; font-size: 0.9rem; }
.secondary-label { flex-shrink: 0; width: 4rem; font-size: 0.7rem; font-weight: 600; text-transform: uppercase; letter-spacing: 0.08em; color: var(--text-dim); padding-top: 0.15rem; }
.pr-link { color: var(--blue); text-decoration: none; word-break: break-all; }
.pr-link:hover { text-decoration: underline; }
.failure-row .failure-reason { color: var(--red); word-break: break-word; }
.freeze-path { color: var(--text-muted); word-break: break-all; }
.empty-state { text-align: center; padding: 4rem 1rem; color: var(--text-muted); }
.empty-prompt { font-family: var(--font-mono); font-size: 3rem; color: var(--accent-dim); margin-bottom: 0.5rem; }
.empty-state h2 { margin: 0 0 0.5rem; color: var(--text); font-size: 1.25rem; }
.empty-state p { margin: 0 auto; max-width: 24rem; }
.load-more-row { display: flex; justify-content: center; padding: 1.5rem 0 0.5rem; }
.load-more { padding: 0.6rem 1.4rem; font-size: 0.9rem; font-weight: 600; color: var(--text); background: var(--surface-2); border: 1px solid var(--border); border-radius: var(--radius); cursor: pointer; transition: border-color 0.2s ease; }
.load-more:hover:not(:disabled) { border-color: var(--text-dim); }
.load-more:disabled { opacity: 0.6; cursor: default; }
/* Skin lives in _shared/components/ErrorBanner.vue; these are this view's
   page-level geometry. */
.error-banner { margin-bottom: 1.5rem; padding: 1rem 1.25rem; }
.mono { font-family: var(--font-mono); }
</style>
