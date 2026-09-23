<script setup>
import { ref, onMounted, onUnmounted } from "vue";

const props = defineProps({
  state: { type: Object, required: true },
  workflows: { type: Array, required: true },
  initialOpen: { type: Boolean, default: false },
});
const emit = defineEmits(["pick"]);

const open = ref(props.initialOpen);
const wrap = ref(null);

function toggle() {
  open.value = !open.value;
}

function close() {
  open.value = false;
}

function pick(name) {
  emit("pick", name);
  open.value = false;
}

function onDocumentClick(e) {
  if (open.value && wrap.value && !wrap.value.contains(e.target)) {
    open.value = false;
  }
}

onMounted(() => document.addEventListener("click", onDocumentClick));
onUnmounted(() => document.removeEventListener("click", onDocumentClick));
</script>

<template>
  <div v-if="state.kind === 'ready'" ref="wrap" class="menu-wrap">
    <button
      class="workflow-trigger"
      aria-haspopup="listbox"
      :aria-expanded="open"
      @click="toggle"
      @keyup.escape="close"
    >
      {{ state.name }} <span class="muted">· {{ state.stageCount }}</span>
      <span class="chev">▾</span>
    </button>
    <ul v-if="open" class="workflow-menu" role="listbox">
      <li v-for="w in workflows" :key="w.name" role="option" :aria-selected="w.name === state.name">
        <button
          class="workflow-option"
          :class="{ active: w.name === state.name }"
          @click="pick(w.name)"
        >
          <span class="check">{{ w.name === state.name ? "✓" : "" }}</span>
          <span class="name">{{ w.name }}</span>
          <span class="count">{{ w.stageCount }}</span>
        </button>
      </li>
    </ul>
  </div>
  <span v-else-if="state.kind === 'frozen'" class="workflow-frozen"
    >{{ state.name }} <span class="muted">· {{ state.stageCount }}</span>
    <span class="frozen-note">(frozen)</span></span
  >
  <span v-else class="workflow-missing">{{ state.name }} (no catalog)</span>
</template>

<style scoped>
.menu-wrap {
  position: relative;
  flex-shrink: 0;
  margin-left: auto;
}

.workflow-trigger {
  background: none;
  border: none;
  padding: 0;
  font-size: 0.85rem;
  color: var(--text);
  cursor: pointer;
  white-space: nowrap;
}

.workflow-trigger:hover {
  text-decoration: underline;
}

.muted {
  color: var(--text-muted);
  font-size: 0.8rem;
}

.chev {
  color: var(--text-muted);
  font-size: 0.75em;
}

.workflow-menu {
  position: absolute;
  top: 100%;
  right: 0;
  z-index: 50;
  list-style: none;
  margin: 0.25rem 0 0;
  padding: 0.25rem 0;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  min-width: 13rem;
  box-shadow: var(--shadow);
}

.workflow-option {
  display: flex;
  gap: 0.5rem;
  align-items: baseline;
  width: 100%;
  background: none;
  border: none;
  padding: 0.4rem 0.75rem;
  font-size: 0.85rem;
  color: var(--text);
  cursor: pointer;
  text-align: left;
}

.workflow-option:hover {
  background: rgba(127, 127, 127, 0.12);
}

.workflow-option.active {
  font-weight: 700;
}

.check {
  width: 1em;
  color: var(--accent);
}

.name {
  flex: 1;
}

.count {
  color: var(--text-muted);
  font-size: 0.78rem;
}

.workflow-frozen {
  flex-shrink: 0;
  margin-left: auto;
  font-size: 0.85rem;
  color: var(--text);
  white-space: nowrap;
}

.frozen-note {
  color: var(--text-muted);
  font-size: 0.8rem;
}

.workflow-missing {
  flex-shrink: 0;
  margin-left: auto;
  font-size: 0.85rem;
  font-style: italic;
  color: var(--text-muted);
  white-space: nowrap;
}
</style>
