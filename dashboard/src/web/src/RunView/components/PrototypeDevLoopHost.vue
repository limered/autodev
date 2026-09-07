<script setup>
import { computed, onMounted, onUnmounted, ref } from "vue";
import RunCard from "./RunCard.vue";

// PROTOTYPE — throwaway for #137 (branch prototype/dev-loop-timeline, do not
// promote as-is). Round 2: B rail won, tables dropped. Three rail variations
// (B1/B2/B3) with model per step, full-width rows, step-to-step arrows and a
// loop-back arrow for quality-loop iterations. ?variant=B1|B2|B3 (B → B1).

const VARIANTS = [
  { key: "B1", name: "Spine" },
  { key: "B2", name: "Chain" },
  { key: "B3", name: "Loop box" },
];

const showPrototype = import.meta.env.DEV;
const open = ref(true);
const variantKey = ref("B1");

function normalize(v) {
  const u = String(v ?? "B1").toUpperCase();
  if (u === "B") return "B1";
  return VARIANTS.some((x) => x.key === u) ? u : "B1";
}

function readVariant() {
  try {
    return normalize(new URLSearchParams(window.location.search).get("variant"));
  } catch {
    return "B1";
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
  { agent: "feature-builder", iteration: null, status: "done", model: "claude-opus", inputTokens: 18200, outputTokens: 6400, durationMs: 9 * 60 * 1000 + 12000 },
  { agent: "test-runner", iteration: null, status: "done", model: "claude-sonnet", inputTokens: 9400, outputTokens: 1800, durationMs: 3 * 60 * 1000 + 40000 },
  { agent: "static-analysis", iteration: 0, status: "done", model: "claude-haiku", inputTokens: 6100, outputTokens: 900, durationMs: 1 * 60 * 1000 + 8000 },
  { agent: "fix-findings", iteration: 0, status: "done", model: "claude-sonnet", inputTokens: 12300, outputTokens: 4100, durationMs: 5 * 60 * 1000 + 20000 },
  { agent: "static-analysis", iteration: 1, status: "done", model: "claude-haiku", inputTokens: 6200, outputTokens: 400, durationMs: 1 * 60 * 1000 + 5000 },
  { agent: "test-runner", iteration: null, status: "done", model: "claude-sonnet", inputTokens: 9600, outputTokens: 700, durationMs: 3 * 60 * 1000 + 10000 },
  { agent: "agentic-review", iteration: null, status: "done", model: "claude-opus", inputTokens: 14800, outputTokens: 2300, durationMs: 4 * 60 * 1000 + 30000 },
  { agent: "pr-author", iteration: null, status: "done", model: "claude-sonnet", inputTokens: 7200, outputTokens: 1900, durationMs: 2 * 60 * 1000 + 15000 },
];

const loopSteps = computed(() => steps.map((s, i) => ({ ...s, idx: i })).filter((s) => s.iteration !== null));

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
</script>

<template>
  <section v-if="showPrototype" class="proto" aria-label="Dev-loop detail prototype">
    <div class="proto-flag mono">PROTOTYPE #137 r2 — throwaway, ?variant=B1|B2|B3</div>
    <RunCard :run="mockRun" :now="now" />
    <button type="button" class="proto-toggle mono" @click="open = !open">
      {{ open ? "▾ hide dev-loop detail" : "▸ show dev-loop detail" }} · {{ steps.length }} steps ·
      {{ fmtTokens(totals.tokens) }} tokens · {{ fmtMs(totals.ms) }}
    </button>

    <!-- B1: connected spine — dots joined by ↓, loop rows carry a ↺ badge -->
    <ol v-if="open && variant === 'B1'" class="spine">
      <li v-for="(s, i) in steps" :key="i" class="sp-row">
        <span class="sp-rail" aria-hidden="true"><span class="sp-dot"></span><span v-if="i < steps.length - 1" class="sp-arrow">↓</span></span>
        <div class="sp-body">
          <div class="sp-top">
            <span class="sp-agent mono">{{ label(s) }}</span>
            <span v-if="isLoop(s)" class="sp-loop mono">↺ loop</span>
            <span class="sp-model mono">{{ s.model }}</span>
          </div>
          <div class="sp-sub mono">{{ fmtTokens(s.inputTokens + s.outputTokens) }} tok · {{ fmtMs(s.durationMs) }} · {{ s.status }}</div>
        </div>
      </li>
    </ol>

    <!-- B2: full-width chain cards with ↓ connectors between them -->
    <ol v-else-if="open && variant === 'B2'" class="chain">
      <li v-for="(s, i) in steps" :key="i" class="ch-item">
        <div class="ch-card" :class="{ 'ch-loop': isLoop(s) }">
          <div class="ch-top">
            <span class="ch-agent mono">{{ isLoop(s) ? `↺ ${label(s)}` : label(s) }}</span>
            <span class="ch-right mono">{{ fmtTokens(s.inputTokens + s.outputTokens) }} · {{ fmtMs(s.durationMs) }}</span>
          </div>
          <div class="ch-sub mono">{{ s.model }} · {{ s.status }}</div>
        </div>
        <div v-if="i < steps.length - 1" class="ch-arrow" aria-hidden="true">↓</div>
      </li>
    </ol>

    <!-- B3: loop iterations grouped in a ↺ loop box, rest as full-width rail rows -->
    <div v-else-if="open && variant === 'B3'" class="lbox">
      <ol class="lbox-list">
        <li v-for="(s, i) in steps.slice(0, 2)" :key="i" class="lbox-row">
          <span class="lbox-agent mono">{{ label(s) }}</span>
          <span class="lbox-model mono">{{ s.model }}</span>
          <span class="lbox-right mono">{{ fmtTokens(s.inputTokens + s.outputTokens) }} · {{ fmtMs(s.durationMs) }}</span>
        </li>
      </ol>
      <div class="lbox-arrow" aria-hidden="true">↓ into quality-loop</div>
      <div class="lbox-loop">
        <div class="lbox-loophead mono">↺ quality-loop ×{{ loopSteps.length }}</div>
        <ol class="lbox-list">
          <li v-for="s in loopSteps" :key="s.idx" class="lbox-row lbox-inner">
            <span class="lbox-agent mono">{{ label(s) }}</span>
            <span class="lbox-model mono">{{ s.model }}</span>
            <span class="lbox-right mono">{{ fmtTokens(s.inputTokens + s.outputTokens) }} · {{ fmtMs(s.durationMs) }}</span>
          </li>
        </ol>
        <div class="lbox-loopfoot mono">↺ loop back until static-analysis passes</div>
      </div>
      <div class="lbox-arrow" aria-hidden="true">↓ out of loop</div>
      <ol class="lbox-list">
        <li v-for="(s, i) in steps.slice(2 + loopSteps.length)" :key="i" class="lbox-row">
          <span class="lbox-agent mono">{{ label(s) }}</span>
          <span class="lbox-model mono">{{ s.model }}</span>
          <span class="lbox-right mono">{{ fmtTokens(s.inputTokens + s.outputTokens) }} · {{ fmtMs(s.durationMs) }}</span>
        </li>
      </ol>
    </div>

    <div class="proto-switcher mono" role="navigation" aria-label="Prototype variant switcher">
      <button type="button" aria-label="Previous variant" @click="step(-1)">←</button>
      <button type="button" :aria-current="variant === 'B1' ? 'true' : null" @click="setVariant('B1')">B1 Spine</button>
      <button type="button" :aria-current="variant === 'B2' ? 'true' : null" @click="setVariant('B2')">B2 Chain</button>
      <button type="button" :aria-current="variant === 'B3' ? 'true' : null" @click="setVariant('B3')">B3 Loop</button>
      <span class="proto-current">{{ variant }} — {{ variantName }}</span>
      <button type="button" aria-label="Next variant" @click="step(1)">→</button>
    </div>
  </section>
</template>

<style scoped>
.proto { margin-top: 2rem; border: 2px dashed var(--amber); border-radius: var(--radius); padding: 1rem; }
.proto-flag { color: var(--amber); font-size: 0.75rem; margin-bottom: 0.75rem; }
.proto-toggle { margin-top: 0.75rem; width: 100%; text-align: left; background: var(--surface-2); color: var(--text); border: 1px solid var(--border); border-radius: var(--radius); padding: 0.5rem 0.75rem; cursor: pointer; font-size: 0.85rem; }
.spine { list-style: none; margin: 0.75rem 0 0; padding: 0; }
.sp-row { display: flex; gap: 0.6rem; }
.sp-rail { display: flex; flex-direction: column; align-items: center; width: 1rem; flex-shrink: 0; }
.sp-dot { width: 0.6rem; height: 0.6rem; margin-top: 0.35rem; border-radius: 50%; background: var(--blue); }
.sp-arrow { color: var(--text-dim); font-size: 0.8rem; line-height: 1.6; }
.sp-body { flex: 1; min-width: 0; padding-bottom: 0.35rem; }
.sp-top { display: flex; align-items: baseline; gap: 0.5rem; }
.sp-agent { font-size: 0.88rem; font-weight: 600; }
.sp-loop { color: var(--amber); font-size: 0.75rem; }
.sp-model { margin-left: auto; color: var(--text-muted); font-size: 0.78rem; }
.sp-sub { color: var(--text-muted); font-size: 0.78rem; }
.chain { list-style: none; margin: 0.75rem 0 0; padding: 0; }
.ch-item { margin: 0; }
.ch-card { border: 1px solid var(--border); border-radius: var(--radius); padding: 0.5rem 0.75rem; background: var(--surface-2); }
.ch-loop { border-left: 3px solid var(--amber); }
.ch-top { display: flex; justify-content: space-between; gap: 0.75rem; }
.ch-agent { font-weight: 600; font-size: 0.88rem; }
.ch-right { color: var(--text); font-size: 0.82rem; white-space: nowrap; }
.ch-sub { color: var(--text-muted); font-size: 0.78rem; margin-top: 0.15rem; }
.ch-arrow { text-align: center; color: var(--text-dim); line-height: 1.4; }
.lbox { margin-top: 0.75rem; }
.lbox-list { list-style: none; margin: 0; padding: 0; }
.lbox-row { display: flex; align-items: baseline; gap: 0.5rem; padding: 0.4rem 0; border-bottom: 1px solid var(--border); }
.lbox-agent { font-weight: 600; font-size: 0.88rem; }
.lbox-model { color: var(--text-muted); font-size: 0.78rem; }
.lbox-right { margin-left: auto; font-size: 0.82rem; white-space: nowrap; }
.lbox-arrow { text-align: center; color: var(--text-dim); font-size: 0.8rem; padding: 0.25rem 0; }
.lbox-loop { border: 1px dashed var(--amber); border-radius: var(--radius); padding: 0.5rem 0.75rem; }
.lbox-loophead { color: var(--amber); font-size: 0.8rem; margin-bottom: 0.25rem; }
.lbox-loopfoot { color: var(--amber); font-size: 0.78rem; margin-top: 0.25rem; }
.lbox-inner { border-bottom-color: var(--border); }
.proto-switcher { position: fixed; bottom: 1rem; left: 50%; transform: translateX(-50%); display: flex; align-items: center; gap: 0.5rem; background: #000; color: #fff; border: 2px solid #fff; border-radius: 999px; padding: 0.4rem 0.8rem; z-index: 50; box-shadow: 0 8px 24px rgba(0,0,0,0.5); }
.proto-switcher button { background: transparent; color: #fff; border: 1px solid transparent; border-radius: 999px; padding: 0.2rem 0.5rem; cursor: pointer; font: inherit; }
.proto-switcher button[aria-current="true"] { border-color: #fff; }
.proto-current { font-size: 0.8rem; opacity: 0.9; }
.mono { font-family: var(--font-mono); }
</style>
