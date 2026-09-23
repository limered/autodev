import { ref, onMounted, getCurrentInstance } from "vue";

export function useFactoryWorkflows(fetchFn = fetch) {
  const catalog = ref(null);
  const error = ref(null);
  const isLoading = ref(false);

  async function load() {
    isLoading.value = true;
    try {
      const res = await fetchFn();
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      catalog.value = await res.json();
      error.value = null;
    } catch (e) {
      error.value = e.message;
    } finally {
      isLoading.value = false;
    }
  }

  if (getCurrentInstance()) {
    onMounted(load);
  }

  return { catalog, error, isLoading, load };
}
