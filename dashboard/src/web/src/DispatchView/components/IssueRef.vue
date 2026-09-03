<script setup>
import { repoColor } from "../../_shared/models/repoColor.js";

// The issue-reference block shared by the DispatchView columns: the issue
// title link plus the repo-ref line under it — repo slug in its identity
// color, then #number — with the skin that block wears in either column.
// One component so the two columns cannot drift apart; single theme, so it
// lives here rather than in _shared. Fragment root on purpose: it renders
// exactly the two elements the columns used to inline, so no wrapper
// appears in the DOM.
defineProps({
  // GitHub issue URL the title link opens.
  htmlUrl: { type: String, required: true },
  // Issue title, shown as the row's heading link.
  title: { type: String, required: true },
  // Repo slug (`owner/name`), colored by its stable repo identity color.
  repo: { type: String, required: true },
  // Issue number, rendered after the slug as #number.
  number: { type: Number, required: true },
});
</script>

<template>
  <a :href="htmlUrl" target="_blank" rel="noopener" class="issue-title">
    {{ title }}
  </a>
  <span class="issue-ref mono">
    <span class="issue-repo" :style="{ color: repoColor(repo) }">{{ repo }}</span
    >#{{ number }}
  </span>
</template>

<style scoped>
.issue-title {
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
  font-size: 0.8rem;
  color: var(--text-muted);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mono {
  font-family: var(--font-mono);
}
</style>
