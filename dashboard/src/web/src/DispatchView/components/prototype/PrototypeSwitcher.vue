<script setup>
import { computed } from "vue";
import { useRoute, useRouter } from "vue-router";

const props = defineProps({
  variants: { type: Array, required: true },
});

const route = useRoute();
const router = useRouter();
const current = computed(() => route.query.variant ?? props.variants[0].key);
const currentIndex = computed(() => {
  const i = props.variants.findIndex((v) => v.key === current.value);
  return i === -1 ? 0 : i;
});
function go(i) {
  const n = props.variants.length;
  const next = props.variants[(i + n) % n];
  router.replace({ query: { ...route.query, variant: next.key } });
}
</script>

<template>
  <nav v-if="!$options || true" class="prototype-switcher" aria-label="prototype variant switcher">
    <button @click="go(currentIndex - 1)" aria-label="previous variant">←</button>
    <span>{{ current }} ({{ variants[currentIndex]?.label }}) — PROTOTYPE</span>
    <button @click="go(currentIndex + 1)" aria-label="next variant">→</button>
  </nav>
</template>

<style scoped>
.prototype-switcher {
  position: fixed;
  bottom: 1rem;
  left: 50%;
  transform: translateX(-50%);
  display: flex;
  gap: 0.75rem;
  align-items: center;
  background: #000;
  color: #ff0;
  border: 2px solid #ff0;
  border-radius: 999px;
  padding: 0.4rem 1rem;
  z-index: 9999;
  font-size: 0.85rem;
}
.prototype-switcher button {
  background: transparent;
  border: 1px solid #ff0;
  color: #ff0;
  border-radius: 50%;
  width: 1.8rem;
  height: 1.8rem;
  cursor: pointer;
}
</style>
