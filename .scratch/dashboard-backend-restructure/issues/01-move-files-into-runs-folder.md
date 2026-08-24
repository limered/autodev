# 01 — Move files into Runs/ feature folder

**What to build:** The Api project groups run code by feature, not by kind. `Models/`, `Store/`, and `Folding/` collapse into a single flat `Runs/` folder that mirrors the frontend's `RunView/` theme, under one `Api.Runs` namespace. Pure move — no behavior changes. This is the prefactor that lets the remaining tickets land in the right place.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] `RunState.cs`, `RunEvent.cs` (was `Models/`), `RunFold.cs` (was `Folding/`), and `RunStore.cs` (was `Store/`) all live directly under `Runs/`, flat — no by-kind sub-folders.
- [ ] All moved types use the `Api.Runs` namespace; all references updated.
- [ ] No logic changes — diff is moves, namespace, and using directives only.
- [ ] `dotnet test dashboard/src/Api.Tests` stays green with `RunFoldTests` unchanged.
