<script setup>
import { computed, onMounted, onUnmounted, ref } from "vue";
import RunCard from "./RunCard.vue";

// PROTOTYPE — throwaway for #137 (branch prototype/dev-loop-timeline, do not
// promote as-is). Three variants of the expandable dev-loop detail,
// switchable via ?variant=A|B|C on the existing / route (sub-shape A).
// Assumed step shape: {agent, iteration, status, inputTokens, outputTokens, durationMs}.
// Collapsed card stays as today via the real RunCard; only the expanded detail swaps.

const VARIANTS = [
  { key: "A", name: "Table" },
  { key: "B", name: "Rail" },
  { key: "C", name: "Grouped loop" },
];

const showPrototype = import.meta.env.DEV;
const open = ref(true);
const variantKey = ref("A");

function readVariant() {
  try {
    const v = String(new URLSearchParams(window.location.search).get("variant") ?? "A").toUpperCase();
    return VARIANTS.some((x) => x.key === v) ? v : "A";
  } catch {
    return "A";
  }
}

const variant = computed(() => variantKey.value);
const variantName = computed(() => VARIANTS.find((x) => x.key === variant.value)?.name ?? "");

function setVariant(key) {
  variantKey.value = key;
  try {
    const url = new URL(window.location.href);
    url.searchParams.set("variant", key);
    window.history.replaceState(null, "", url);
  } catch {
    // ponytail: prototype-only URL share; ignore when no window/history
  }
}
function step(d) {
  const i = VARIANTS.findIndex((x) => x.key === variant.value);
  setVariant(VARIANTS[(i + d + VARIANTS.length) % VARIANTS.length].key);
}
function onKey(e) {
  const t = e.target;
  if (t && (t.tagName === "INPUT" || t.tagName === "TEXTAREA" || t.isContentEditable)) return;
  if (e.key !== "ArrowLeft" && e.key !== "ArrowRight") return;
  try {
    if (!new URLSearchParams(window.location.search).has("variant")) return;
  } catch {
    return;
  }
  e.preventDefault();
  step(e.key === "ArrowRight" ? 1 : -1);
}
onMounted(() => {
  variantKey.value = readVariant();
  window.addEventListener("keydown", onKey);
});
onUnmounted(() => window.removeEventListener("keydown", onKey));

const now = Date.now();
const mockRun = {
  runId: "prototype",
  status: "done",
  repo: "limered/autodev",
  branch: "prototype/dev-loop-timeline",
  vmName: "factory-proto",
  prUrl: "https://github.com/limered/autodev/pull/0",
  failureReason: null,
  freezeCaptured: false,
  startedAt: new Date(now - 42 * 60 * 1000).toISOString(),
  finishedAt: new Date(now - 4 * 60 * 1000).toISOString(),
  lastHeartbeatAt: new Date(now - 4 * 60 * 1000).toISOString(),
  stages: [],
};

const steps = [
  { agent: "feature-builder", iteration: null, status: "done", inputTokens: 18200, outputTokens: 6400, durationMs: 9 * 60 * 1000 + 12000 },
  { agent: "test-runner", iteration: null, status: "done", inputTokens: 9400, outputTokens: 1800, durationMs: 3 * 60 * 1000 + 40000 },
  { agent: "static-analysis", iteration: 0, status: "done", inputTokens: 6100, outputTokens: 900, durationMs: 1 * 60 * 1000 + 8000 },
  { agent: "fix-findings", iteration: 0, status: "done", inputTokens: 12300, outputTokens: 4100, durationMs: 5 * 60 * 1000 + 20000 },
  { agent: "static-analysis", iteration: 1, status: "done", inputTokens: 6200, outputTokens: 400, durationMs: 1 * 60 * 1000 + 5000 },
  { agent: "test-runner", iteration: null, status: "done", inputTokens: 9600, outputTokens: 700, durationMs: 3 * 60 * 1000 + 10000 },
  { agent: "agentic-review", iteration: null, status: "done", inputTokens: 14800, outputTokens: 2300, durationMs: 4 * 60 * 1000 + 30000 },
  { agent: "pr-author", iteration: null, status: "done", inputTokens: 7200, outputTokens: 1900, durationMs: 2 * 60 * 1000 + 15000 },
];

const totals = computed(() => ({
  tokens: steps.reduce((n, s) => n + s.inputTokens + s.outputTokens, 0),
  ms: steps.reduce((n, s) => n + s.durationMs, 0),
}));

function fmtTokens(n) {
  return n >= 1000 ? `${(n / 1000).toFixed(1)}k` : String(n);
}
function fmtMs(ms) {
  const s = Math.round(ms / 1000);
  if (s < 60) return `${s}s`;
  return `${Math.floor(s / 60)}m ${String(s % 60).padStart(2, "0")}s`;
}
function label(s) {
  return s.iteration === null ? s.agent : `${s.agent} #${s.iteration + 1}`;
}
</script>

<template>
  <section v-if="showPrototype" class="proto" aria-label="Dev-loop detail prototype">
    <div class="proto-flag mono">PROTOTYPE #137 — throwaway, ?variant=A|B|C</div>
    <RunCard :run="mockRun" :now="now" />
    <button type="button" class="proto-toggle mono" @click="open = !open">
      {{ open ? "▾ hide dev-loop detail" : "▸ show dev-loop detail" }} · {{ steps.length }} steps ·
      {{ fmtTokens(totals.tokens) }} tokens · {{ fmtMs(totals.ms) }}
    </button>

    <!-- Variant A: flat table, every step (incl. each loop iteration) its own row -->
    <table v-if="open && variant === 'A'" class="a-table">
      <thead>
        <tr><th>Step</th><th>Status</th><th class="num">Tokens</th><th class="num">Duration</th></tr>
      </thead>
      <tbody>
        <tr v-for="s in steps" :key="label(s)">
          <td class="mono">{{ label(s) }}</td>
          <td>{{ s.status }}</td>
          <td class="num mono">{{ fmtTokens(s.inputTokens + s.outputTokens) }}</td>
          <td class="num mono">{{ fmtMs(s.durationMs) }}</td>
        </tr>
      </tbody>
      <tfoot>
        <tr><td class="mono">total</td><td></td><td class="num mono">{{ fmtTokens(totals.tokens) }}</td><td class="num mono">{{ fmtMs(totals.ms) }}</td></tr>
      </tfoot>
    </table>

    <!-- Variant B: vertical rail, loop iterations indented under a loop header -->
    <ol v-else-if="open && variant === 'B'" class="b-rail">
      <li v-for="s in steps" :key="label(s)" class="b-row" :class="{ 'b-loop': s.iteration !== null }">
        <span class="b-dot" aria-hidden="true"></span>
        <div class="b-main">
          <span class="b-agent mono">{{ label(s) }}</span>
          <span class="b-sub mono">{{ fmtTokens(s.inputTokens + s.outputTokens) }} tok · {{ fmtMs(s.durationMs) }} · {{ s.status }}</span>
        </div>
      </li>
    </ol>

    <!-- Variant C: summary + grouped loop in a nested disclosure -->
    <div v-else-if="open && variant === 'C'" class="c-wrap">
      <div class="c-summary mono">
        {{ steps.length }} steps · {{ fmtTokens(totals.tokens) }} tokens · {{ fmtMs(totals.ms) }}
      </div>
      <ul class="c-list">
        <li v-for="s in steps.filter((x) => x.iteration === null)" :key="s.agent" class="c-row mono">
          <span>{{ s.agent }}</span><span>{{ fmtTokens(s.inputTokens + s.outputTokens) }} · {{ fmtMs(s.durationMs) }}</span>
        </li>
      </ul>
      <details class="c-loop">
        <summary class="mono">quality-loop ×{{ steps.filter((x) => x.iteration !== null).length }} — {{ fmtMs(steps.filter((x) => x.iteration !== null).reduce((n, x) => n + x.durationMs, 0)) }}</summary>
        <ul class="c-list">
          <li v-for="s in steps.filter((x) => x.iteration !== null)" :key="label(s)" class="c-row mono">
            <span>{{ label(s) }}</span><span>{{ fmtTokens(s.inputTokens + s.outputTokens) }} · {{ fmtMs(s.durationMs) }}</span>
          </li>
        </ul>
      </details>
    </div>

    <div class="proto-switcher mono" role="navigation" aria-label="Prototype variant switcher">
      <button type="button" aria-label="Previous variant" @click="step(-1)">←</button>
      <button type="button" :aria-current="variant === 'A' ? 'true' : null" @click="setVariant('A')">A Table</button>
      <button type="button" :aria-current="variant === 'B' ? 'true' : null" @click="setVariant('B')">B Rail</button>
      <button type="button" :aria-current="variant === 'C' ? 'true' : null" @click="setVariant('C')">C Grouped</button>
      <span class="proto-current">{{ variant }} — {{ variantName }}</span>
      <button type="button" aria-label="Next variant" @click="step(1)">→</button>
    </div>
  </section>
</template>

<style scoped>
.proto { margin-top: 2rem; border: 2px dashed var(--amber); border-radius: var(--radius); padding: 1rem; }
.proto-flag { color: var(--amber); font-size: 0.75rem; margin-bottom: 0.75rem; }
.proto-toggle { margin-top: 0.75rem; width: 100%; text-align: left; background: var(--surface-2); color: var(--text); border: 1px solid var(--border); border-radius: var(--radius); padding: 0.5rem 0.75rem; cursor: pointer; font-size: 0.85rem; }
.a-table { width: 100%; margin-top: 0.5rem; border-collapse: collapse; font-size: 0.85rem; }
.a-table th, .a-table td { text-align: left; padding: 0.4rem 0.6rem; border-bottom: 1px solid var(--border); }
.a-table .num { text-align: right; }
.b-rail { list-style: none; margin: 0.75rem 0 0; padding: 0; }
.b-row { display: flex; gap: 0.6rem; padding: 0.35rem 0; }
.b-loop { margin-left: 1.25rem; }
.b-dot { width: 0.6rem; height: 0.6rem; margin-top: 0.35rem; border-radius: 50%; background: var(--blue); flex-shrink: 0; }
.b-main { display: flex; flex-direction: column; }
.b-sub { color: var(--text-muted); font-size: 0.78rem; }
.c-wrap { margin-top: 0.5rem; font-size: 0.85rem; }
.c-summary { color: var(--text-muted); margin-bottom: 0.5rem; }
.c-list { list-style: none; margin: 0; padding: 0; }
.c-row { display: flex; justify-content: space-between; padding: 0.35rem 0; border-bottom: 1px solid var(--border); }
.c-loop { margin-top: 0.5rem; }
.c-loop summary { cursor: pointer; color: var(--cyan); }
.proto-switcher { position: fixed; bottom: 1rem; left: 50%; transform: translateX(-50%); display: flex; align-items: center; gap: 0.5rem; background: #000; color: #fff; border: 2px solid #fff; border-radius: 999px; padding: 0.4rem 0.8rem; z-index: 50; box-shadow: 0 8px 24px rgba(0,0,0,0.5); }
.proto-switcher button { background: transparent; color: #fff; border: 1px solid transparent; border-radius: 999px; padding: 0.2rem 0.5rem; cursor: pointer; font: inherit; }
.proto-switcher button[aria-current="true"] { border-color: #fff; }
.proto-current { font-size: 0.8rem; opacity: 0.9; }
.mono { font-family: var(--font-mono); }
</style>
