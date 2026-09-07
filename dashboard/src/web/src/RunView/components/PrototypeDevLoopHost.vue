<script setup>
import { computed, onMounted, onUnmounted, ref } from "vue";
import RunCard from "./RunCard.vue";

// PROTOTYPE — throwaway for #137 (branch prototype/dev-loop-timeline, do not
// promote as-is). Round 3: B2 chain won. Three compact variants (C1/C2/C3):
// status as color only (no "done" text; pending grey, running green, done
// blue — same seam as RunCard stage badges), chips fit their content so the
// model text sets the length. ?variant=C1|C2|C3 (legacy B* → C1).

const VARIANTS = [
  { key: "C1", name: "Compact chain" },
  { key: "C2", name: "Flow" },
  { key: "C3", name: "Stepped rail" },
];

const showPrototype = import.meta.env.DEV;
const open = ref(true);
const variantKey = ref("C1");

function normalize(v) {
  const u = String(v ?? "C1").toUpperCase();
  if (u.startsWith("B")) return "C1";
  return VARIANTS.some((x) => x.key === u) ? u : "C1";
}

function readVariant() {
  try {
    return normalize(new URLSearchParams(window.location.search).get("variant"));
  } catch {
    return "C1";
  }
}

const variant = computed(() => variantKey.value);
const variantName = computed(() => VARIANTS.find((x) => x.key === variant.value)?.name ?? "");

function setVariant(key) {
  variantKey.value = normalize(key);
  try {
    const url = new URL(window.location.href);
    url.searchParams.set("variant", variantKey.value);
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
  status: "running",
  repo: "limered/autodev",
  branch: "prototype/dev-loop-timeline",
  vmName: "factory-proto",
  prUrl: null,
  failureReason: null,
  freezeCaptured: false,
  startedAt: new Date(now - 42 * 60 * 1000).toISOString(),
  finishedAt: null,
  lastHeartbeatAt: new Date(now - 3000).toISOString(),
  stages: [],
};

// Mixed statuses to show the color coding (active run mid quality-loop).
// Long model ids on purpose: chip length follows the text.
const steps = [
  { agent: "feature-builder", iteration: null, status: "done", model: "anthropic/claude-opus-4-1", inputTokens: 18200, outputTokens: 6400, durationMs: 9 * 60 * 1000 + 12000 },
  { agent: "test-runner", iteration: null, status: "done", model: "openai/gpt-5", inputTokens: 9400, outputTokens: 1800, durationMs: 3 * 60 * 1000 + 40000 },
  { agent: "static-analysis", iteration: 0, status: "done", model: "google/gemini-2.5-flash", inputTokens: 6100, outputTokens: 900, durationMs: 1 * 60 * 1000 + 8000 },
  { agent: "fix-findings", iteration: 0, status: "running", model: "anthropic/claude-sonnet-4", inputTokens: 12300, outputTokens: 4100, durationMs: 5 * 60 * 1000 + 20000 },
  { agent: "static-analysis", iteration: 1, status: "pending", model: "google/gemini-2.5-flash", inputTokens: 0, outputTokens: 0, durationMs: 0 },
  { agent: "test-rerun", iteration: null, status: "pending", model: "openai/gpt-5", inputTokens: 0, outputTokens: 0, durationMs: 0 },
  { agent: "agentic-review", iteration: null, status: "pending", model: "anthropic/claude-opus-4-1", inputTokens: 0, outputTokens: 0, durationMs: 0 },
  { agent: "pr-author", iteration: null, status: "pending", model: "xai/grok-code-fast-1", inputTokens: 0, outputTokens: 0, durationMs: 0 },
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
function isLoop(s) {
  return s.iteration !== null;
}
function stats(s) {
  if (s.status === "pending") return "—";
  return `${fmtTokens(s.inputTokens + s.outputTokens)} · ${fmtMs(s.durationMs)}`;
}
</script>

<template>
  <section v-if="showPrototype" class="proto" aria-label="Dev-loop detail prototype">
    <div class="proto-flag mono">PROTOTYPE #137 r3 — throwaway, ?variant=C1|C2|C3</div>
    <RunCard :run="mockRun" :now="now" />
    <button type="button" class="proto-toggle mono" @click="open = !open">
      {{ open ? "▾ hide dev-loop detail" : "▸ show dev-loop detail" }} · {{ steps.length }} steps ·
      {{ fmtTokens(totals.tokens) }} tokens · {{ fmtMs(totals.ms) }}
    </button>

    <!-- C1: vertical compact chain — fit-content cards joined by ↓ -->
    <ol v-if="open && variant === 'C1'" class="c1">
      <li v-for="(s, i) in steps" :key="i" class="c1-item">
        <div class="chip" :class="[`st-${s.status}`, { loop: isLoop(s) }]" :title="`${label(s)} — ${s.status}`">
          <span class="chip-dot" aria-hidden="true"></span>
          <span class="chip-agent mono">{{ isLoop(s) ? `↺ ${label(s)}` : label(s) }}</span>
          <span class="chip-model mono">{{ s.model }}</span>
          <span class="chip-stats mono">{{ stats(s) }}</span>
        </div>
        <div v-if="i < steps.length - 1" class="c1-arrow" aria-hidden="true">↓</div>
      </li>
    </ol>

    <!-- C2: horizontal wrapping flow — chips connected by → -->
    <div v-else-if="open && variant === 'C2'" class="c2">
      <template v-for="(s, i) in steps" :key="i">
        <div class="chip" :class="[`st-${s.status}`, { loop: isLoop(s) }]" :title="`${label(s)} — ${s.status}`">
          <span class="chip-dot" aria-hidden="true"></span>
          <span class="chip-agent mono">{{ isLoop(s) ? `↺ ${label(s)}` : label(s) }}</span>
          <span class="chip-model mono">{{ s.model }}</span>
          <span class="chip-stats mono">{{ stats(s) }}</span>
        </div>
        <span v-if="i < steps.length - 1" class="c2-arrow" aria-hidden="true">→</span>
      </template>
    </div>

    <!-- C3: stepped rail — spine with ↓, compact cards, loop rows indented -->
    <ol v-else-if="open && variant === 'C3'" class="c3">
      <li v-for="(s, i) in steps" :key="i" class="c3-item" :class="{ 'c3-loop': isLoop(s) }">
        <span class="c3-rail" aria-hidden="true"><span class="chip-dot" :class="`st-${s.status}`"></span><span v-if="i < steps.length - 1" class="c3-arrow">↓</span></span>
        <div class="chip" :class="[`st-${s.status}`, { loop: isLoop(s) }]" :title="`${label(s)} — ${s.status}`">
          <span class="chip-agent mono">{{ isLoop(s) ? `↺ ${label(s)}` : label(s) }}</span>
          <span class="chip-model mono">{{ s.model }}</span>
          <span class="chip-stats mono">{{ stats(s) }}</span>
        </div>
      </li>
    </ol>

    <div class="proto-switcher mono" role="navigation" aria-label="Prototype variant switcher">
      <button type="button" aria-label="Previous variant" @click="step(-1)">←</button>
      <button type="button" :aria-current="variant === 'C1' ? 'true' : null" @click="setVariant('C1')">C1 Chain</button>
      <button type="button" :aria-current="variant === 'C2' ? 'true' : null" @click="setVariant('C2')">C2 Flow</button>
      <button type="button" :aria-current="variant === 'C3' ? 'true' : null" @click="setVariant('C3')">C3 Rail</button>
      <span class="proto-current">{{ variant }} — {{ variantName }}</span>
      <button type="button" aria-label="Next variant" @click="step(1)">→</button>
    </div>
  </section>
</template>

<style scoped>
.proto { margin-top: 2rem; border: 2px dashed var(--amber); border-radius: var(--radius); padding: 1rem; }
.proto-flag { color: var(--amber); font-size: 0.75rem; margin-bottom: 0.75rem; }
.proto-toggle { margin-top: 0.75rem; width: 100%; text-align: left; background: var(--surface-2); color: var(--text); border: 1px solid var(--border); border-radius: var(--radius); padding: 0.5rem 0.75rem; cursor: pointer; font-size: 0.85rem; }
.chip { display: inline-flex; align-items: baseline; gap: 0.5rem; width: fit-content; max-width: 100%; padding: 0.35rem 0.6rem; background: var(--surface-2); border: 1px solid var(--border); border-left: 3px solid var(--st, var(--text-dim)); border-radius: var(--radius); }
.chip.loop { border-style: dashed; border-color: var(--st, var(--text-dim)); }
.chip-dot { width: 0.55rem; height: 0.55rem; border-radius: 50%; background: var(--st, var(--text-dim)); flex-shrink: 0; align-self: center; }
.st-done { --st: var(--blue); }
.st-running { --st: var(--green); }
.st-running.chip-dot, .st-running .chip-dot { animation: blink 1.4s infinite; }
.st-pending { --st: var(--text-dim); }
.st-pending .chip-agent { color: var(--text-muted); }
.chip-agent { font-weight: 600; font-size: 0.85rem; white-space: nowrap; }
.chip-model { color: var(--cyan); font-size: 0.76rem; white-space: nowrap; }
.chip-stats { margin-left: auto; color: var(--text-muted); font-size: 0.78rem; white-space: nowrap; }
@keyframes blink { 0%, 100% { opacity: 1; } 50% { opacity: 0.35; } }
.c1 { list-style: none; margin: 0.75rem 0 0; padding: 0; }
.c1-arrow { color: var(--text-dim); line-height: 1.3; padding-left: 0.4rem; }
.c2 { display: flex; flex-wrap: wrap; align-items: center; gap: 0.35rem; margin-top: 0.75rem; }
.c2-arrow { color: var(--text-dim); }
.c3 { list-style: none; margin: 0.75rem 0 0; padding: 0; }
.c3-item { display: flex; gap: 0.5rem; }
.c3-loop { margin-left: 1.25rem; }
.c3-rail { display: flex; flex-direction: column; align-items: center; width: 1rem; flex-shrink: 0; }
.c3-rail .chip-dot { margin-top: 0.45rem; }
.c3-arrow { color: var(--text-dim); font-size: 0.8rem; line-height: 1.6; }
.c3-item .chip { margin-bottom: 0.15rem; }
.proto-switcher { position: fixed; bottom: 1rem; left: 50%; transform: translateX(-50%); display: flex; align-items: center; gap: 0.5rem; background: #000; color: #fff; border: 2px solid #fff; border-radius: 999px; padding: 0.4rem 0.8rem; z-index: 50; box-shadow: 0 8px 24px rgba(0,0,0,0.5); }
.proto-switcher button { background: transparent; color: #fff; border: 1px solid transparent; border-radius: 999px; padding: 0.2rem 0.5rem; cursor: pointer; font: inherit; }
.proto-switcher button[aria-current="true"] { border-color: #fff; }
.proto-current { font-size: 0.8rem; opacity: 0.9; }
.mono { font-family: var(--font-mono); }
</style>
