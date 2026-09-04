import { createRouter, createWebHistory } from "vue-router";
import RunsList from "./RunView/components/RunsList.vue";
import DispatchView from "./DispatchView/components/DispatchView.vue";

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
  ],
});
