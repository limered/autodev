import { ref } from "vue";

// Per-repo catalog cache for the queue rows' pickers: each row's repo catalog
// (GET /catalogs/{repo}) replaces the factory feed's workflows when the repo
// ships a readable agents.json, so the picker, the muted missing notice, and
// the vanished warning all read the catalog that will actually govern the
// claim. Rows' repos are watched by the caller (queue feed to distinct repos);
// a repo already fetched is not re-fetched — the claim revalidates the catalog
// sha server-side anyway, so the UI stays cheap.
export function useRowCatalogs(fetchFn = fetch) {
  const byRepo = ref({});
  const fetched = new Set();

  async function load(repos) {
    const pending = [...new Set(repos.filter(Boolean))].filter((repo) => !fetched.has(repo));
    if (!pending.length) return;
    await Promise.all(
      pending.map(async (repo) => {
        fetched.add(repo);
        let entry = null;
        try {
          const res = await fetchFn(`/catalogs/${repo}`);
          if (!res.ok) throw new Error(`HTTP ${res.status}`);
          entry = await res.json();
        } catch {
          // No catalog row or failed fetch: the row falls back to the factory
          // feed; a muted notice covers it. A subsequent reload skips the repo
          // either way — the claim revalidates the sha server-side.
          entry = null;
        }
        byRepo.value = { ...byRepo.value, [repo]: entry };
      }),
    );
  }

  return { byRepo, load };
}
