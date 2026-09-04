import { createRouter, createWebHistory } from "vue-router";
import RunsList from "./RunView/components/RunsList.vue";
import DispatchView from "./DispatchView/components/DispatchView.vue";
import RunCardHeaderPrototype from "./Prototype/components/RunCardHeaderPrototype.vue";

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", name: "active", component: RunsList, meta: { title: "active-jobs", hotkey: "1" } },
    {
      path: "/issueview",
      name: "queue",
      component: DispatchView,
      meta: { title: "queue", hotkey: "2" },
    },
    // Throwaway prototype route (issue #99) — exists only on the
    // prototype/runcard-header branch and is removed once a variant wins.
    {
      path: "/prototype/runcard-header",
      name: "prototype-runcard-header",
      component: RunCardHeaderPrototype,
      meta: { title: "runcard-header", hotkey: "3" },
    },
  ],
});
