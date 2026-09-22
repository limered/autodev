// PROTOTYPE — throwaway data stub for issue #234, not production.
// Three variants of the queue-row workflow picker, switchable via ?variant=, on the existing /issueview route.

export const PROTOTYPE_CATALOGS = {
  normal: {
    source: "target-repo agents.json",
    defaultWorkflow: "full",
    workflows: [
      { name: "full", stages: ["implementation", "quality-loop", "test-rerun", "agentic-review", "pr-author"] },
      { name: "architecture-check", stages: ["agentic-review"] },
      { name: "full-minus-tests", stages: ["implementation", "quality-loop", "agentic-review", "pr-author"] },
    ],
  },
  missing: null,
  vanished: {
    source: "target-repo agents.json",
    defaultWorkflow: "full",
    workflows: [
      { name: "full", stages: ["implementation", "quality-loop"] },
    ],
  },
};

export const PROTOTYPE_ROWS = [
  { id: "q1", rank: 1, title: "Add login page", repo: "acme/web", number: 101, runId: null, picked: "full", catalog: "normal" },
  { id: "q2", rank: 2, title: "Fix sync bug", repo: "acme/web", number: 102, runId: "run-abc", picked: "architecture-check", catalog: "normal" },
  { id: "q3", rank: 3, title: "Docs refresh", repo: "other/docs", number: 55, runId: null, picked: "full", catalog: "missing" },
  { id: "q4", rank: 4, title: "Old pick", repo: "acme/web", number: 99, runId: null, picked: "retired-flow", catalog: "vanished" },
];
