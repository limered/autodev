<script setup>
import { useIssuesFeed } from '../services/useIssuesFeed.js'

const { issues, error, isLoading } = useIssuesFeed(() => fetch('/issues'))
</script>

<template>
  <section class="dispatch-panel">
    <header class="panel-header">
      <h2><span class="prompt">&gt;</span> Eligible Issues</h2>
      <div class="connection" :class="{ loading: isLoading }">
        <span class="connection-dot"></span>
        <span v-if="error">sync error</span>
        <span v-else>live</span>
      </div>
    </header>

    <section v-if="error" class="error-banner" role="alert">
      <strong>Sync failed</strong>
      <p>Failed to load eligible issues: {{ error }}</p>
    </section>

    <section v-if="issues.length" class="issue-list">
      <article
        v-for="issue in issues"
        :key="issue.githubId"
        class="issue-row"
      >
        <a :href="issue.htmlUrl" target="_blank" rel="noopener" class="issue-title">
          {{ issue.title }}
        </a>
        <span class="issue-ref mono">{{ issue.repo }}#{{ issue.number }}</span>
      </article>
    </section>

    <section v-else-if="!error" class="empty-state">
      <div class="empty-prompt">&gt;_</div>
      <p>No eligible issues synced yet.</p>
    </section>
  </section>
</template>

<style scoped>
.dispatch-panel {
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.panel-header {
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

.connection-dot {
  width: 0.4rem;
  height: 0.4rem;
  border-radius: 50%;
  background: var(--accent);
}

.error-banner {
  margin-bottom: 1rem;
  padding: 0.75rem 1rem;
  background: rgba(248, 81, 73, 0.12);
  border: 1px solid rgba(248, 81, 73, 0.35);
  border-radius: var(--radius);
  font-size: 0.9rem;
}

.error-banner strong {
  display: block;
  color: var(--red);
  margin-bottom: 0.25rem;
}

.error-banner p {
  margin: 0;
  color: var(--text);
}

.issue-list {
  display: grid;
  gap: 0.5rem;
}

.issue-row {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.75rem 1rem;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius);
}

.issue-row:hover {
  border-color: var(--text-dim);
}

.issue-title {
  min-width: 0;
  color: var(--text);
  text-decoration: none;
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.issue-title:hover {
  text-decoration: underline;
}

.issue-ref {
  flex-shrink: 0;
  font-size: 0.8rem;
  color: var(--text-muted);
}

.empty-state {
  text-align: center;
  padding: 2rem 1rem;
  color: var(--text-muted);
}

.empty-prompt {
  font-family: var(--font-mono);
  font-size: 2rem;
  color: var(--accent-dim);
  margin-bottom: 0.25rem;
}

.empty-state p {
  margin: 0;
}

.mono {
  font-family: var(--font-mono);
}

@media (max-width: 640px) {
  .issue-row {
    flex-direction: column;
    align-items: flex-start;
    gap: 0.25rem;
  }

  .issue-title {
    white-space: normal;
  }
}
</style>
