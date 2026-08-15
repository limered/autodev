# 03 — Define opencode headless job agent

**What to build:** An opencode agent/prompt configuration that runs headlessly inside a job VM, implements a change in a checked-out project repository, pushes the result as a branch, and creates a pull request.

**Blocked by:** 01 — Create multipass blueprint for disposable job VM; 02 — Configure bot Git authentication.

**Status:** done

- [x] An opencode agent or skill configuration exists for the headless feature-implementation workflow.
  - Created `.opencode/opencode.json` with `feature-builder` as the default primary agent.
  - Created `.opencode/agents/feature-builder.md` with the implementation/push/PR workflow prompt.
- [x] A mechanism copies the relevant agents/skills from the `autodev` repo into the project repo inside the VM.
  - `test-feature-builder.ps1` tars `.opencode/` and extracts it into the cloned repo inside the VM.
- [x] Running the agent against a sample repository produces a branch containing the requested change.
  - Verified via `test-feature-builder.ps1` with `opencode-go/kimi-k2.7-code`: branch `factory/test-20260816-012940-5233` pushed to `limered/autodev`.
- [x] The agent creates a pull request for the branch via the GitHub API.
  - Created PR #2: https://github.com/limered/autodev/pull/2

## Blocker (resolved)

The silent ~60s hang was the **model**, not `opencode run`. `test-feature-builder.sh` hardcoded `opencode-go/deepseek-v4-flash`, which returns a region error until opted in (see note below). A model that errors every request loads config, then stalls until cleanup — exactly the observed symptom. `opencode run` is headless by default (`--interactive` defaults to `false`); no HTTP-API workaround is needed.

Fix in `test-feature-builder.sh`:
- Model switched to `opencode-go/grok-4.5` (overridable via `$MODEL`).
- Run now uses `--agent feature-builder --print-logs --format json` and redirects stdin from `/dev/null`, so a stalled or errored run is visible in harness output and nothing can block on a TTY.

## Additional note

The requested model `opencode-go/deepseek-v4-flash` returns a region error unless explicitly opted in via `https://opencode.ai/workspace/wrk_01KKYT9XAMT2D4V2MPDJ2M8Q56/go`. `opencode-go/grok-4.5` works via the HTTP API and can be used for testing until DeepSeek is opted in.
