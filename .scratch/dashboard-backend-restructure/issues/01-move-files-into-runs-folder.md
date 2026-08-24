# 01 — Move files into Runs/ feature folder

**What to build:** The Api project groups run code by feature, not by kind. `Models/`, `Store/`, and `Folding/` collapse into a single flat `Runs/` folder that mirrors the frontend's `RunView/` theme, under one `Api.Runs` namespace. Pure move — no behavior changes. This is the prefactor that lets the remaining tickets land in the right place.

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [x] `RunState.cs`, `RunEvent.cs` (was `Models/`), `RunFold.cs` (was `Folding/`), and `RunStore.cs` (was `Store/`) all live directly under `Runs/`, flat — no by-kind sub-folders.
- [x] All moved types use the `Api.Runs` namespace; all references updated.
- [x] No logic changes — diff is moves, namespace, and using directives only.
- [x] `dotnet test dashboard/src/Api.Tests` stays green with `RunFoldTests` unchanged.
