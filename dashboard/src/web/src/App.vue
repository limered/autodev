<script setup>
import { computed, onMounted, onUnmounted } from "vue";
import { useRoute, useRouter } from "vue-router";

const route = useRoute();
const router = useRouter();

const pages = router.options.routes.map((r) => ({
  path: r.path,
  title: r.meta.title,
  hotkey: r.meta.hotkey,
}));

const currentTitle = computed(() => route.meta.title ?? "");

function onKey(e) {
  const t = e.target;
  if (t && (t.tagName === "INPUT" || t.tagName === "TEXTAREA" || t.isContentEditable)) return;
  const hit = pages.find((p) => p.hotkey === e.key);
  if (hit) {
    router.push(hit.path);
    e.preventDefault();
  }
}
onMounted(() => window.addEventListener("keydown", onKey));
onUnmounted(() => window.removeEventListener("keydown", onKey));
</script>

<template>
  <main class="dashboard">
    <header class="page-header">
      <div class="prompt-line mono">
        <span class="prompt">factory</span><span class="colon">:</span
        ><span class="path">~/{{ currentTitle }}</span
        ><span class="cursor">$</span>
      </div>
      <nav class="segmented mono">
        <RouterLink
          v-for="p in pages"
          :key="p.path"
          class="seg"
          :to="p.path"
          :aria-current="route.path === p.path ? 'page' : null"
        >
          <span class="seg-key">[{{ p.hotkey }}]</span> {{ p.title }}
        </RouterLink>
      </nav>
    </header>

    <RouterView />
  </main>
</template>

<style>
:root {
  --bg: #0b0d10;
  --surface: #11141a;
  --surface-2: #181c24;
  --border: #2a303c;
  --text: #c9d1d9;
  --text-muted: #8b949e;
  --text-dim: #6e7681;
  --accent: #3fb950;
  --accent-dim: #2ea043;
  --cyan: #39c5cf;
  --amber: #d29922;
  --red: #f85149;
  --green: #3fb950;
  --blue: #58a6ff;
  /* Repo identity palette: hues chosen to stay legible on the dark surface
     and clear of the status colors above (accent/green, cyan, blue, red, amber). */
  --repo-violet: #a371f7;
  --repo-lilac: #d2a8ff;
  --repo-magenta: #db61a2;
  --repo-pink: #f778ba;
  --repo-orange: #ffa657;
  --repo-yellow: #f7dd6e;
  --radius: 0.75rem;
  --shadow: 0 8px 24px rgba(0, 0, 0, 0.35);
  --font-sans: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
  --font-mono:
    ui-monospace, SFMono-Regular, "SF Mono", Menlo, Consolas, "Liberation Mono", monospace;
}

* {
  box-sizing: border-box;
}

body {
  margin: 0;
  background: var(--bg);
  color: var(--text);
  font-family: var(--font-sans);
  line-height: 1.5;
}
</style>

<style scoped>
.dashboard {
  max-width: 1100px;
  margin: 0 auto;
  padding: 2rem 1.25rem 4rem;
}

.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
  margin-bottom: 1.75rem;
  padding-bottom: 1rem;
  border-bottom: 1px solid var(--border);
}

.prompt-line {
  font-size: 1.1rem;
}
.prompt {
  color: var(--accent);
  font-weight: 700;
}
.colon {
  color: var(--text-dim);
}
.path {
  color: var(--cyan);
}
.cursor {
  color: var(--text);
  margin-left: 0.25rem;
  animation: blink 1.1s step-end infinite;
}
@keyframes blink {
  50% {
    opacity: 0;
  }
}

.segmented {
  display: inline-flex;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  overflow: hidden;
  background: var(--surface);
}

.seg {
  padding: 0.5rem 0.9rem;
  color: var(--text-muted);
  font-size: 0.85rem;
  font-weight: 600;
  text-decoration: none;
  border-right: 1px solid var(--border);
}
.seg:last-child {
  border-right: none;
}
.seg:hover {
  color: var(--text);
  background: var(--surface-2);
}
.seg.router-link-exact-active {
  color: var(--bg);
  background: var(--accent);
}
.seg-key {
  opacity: 0.7;
}

.mono {
  font-family: var(--font-mono);
}
</style>
