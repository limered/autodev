# 03 — Define opencode headless job agent

**What to build:** An opencode agent/prompt configuration that runs headlessly inside a job VM, implements a change in a checked-out project repository, pushes the result as a branch, and creates a pull request.

**Blocked by:** 01 — Create multipass blueprint for disposable job VM; 02 — Configure bot Git authentication.

**Status:** blocked

- [x] An opencode agent or skill configuration exists for the headless feature-implementation workflow.
  - Created `.opencode/opencode.json` with `feature-builder` as the default primary agent.
  - Created `.opencode/agents/feature-builder.md` with the implementation/push/PR workflow prompt.
- [x] A mechanism copies the relevant agents/skills from the `autodev` repo into the project repo inside the VM.
  - `test-feature-builder.ps1` tars `.opencode/` and extracts it into the cloned repo inside the VM.
- [ ] Running the agent against a sample repository produces a branch containing the requested change.
- [ ] The agent creates a pull request for the branch via the GitHub API.

## Blocker

`opencode run` does not execute headlessly inside the multipass VM. Tests on `factory-probe` show:

- `opencode run --auto "<prompt>"` starts, loads config, then hangs silently for ~60 seconds until cleanup.
- When forced into a pseudo-TTY with `script`, `opencode run --auto` opens the interactive TUI instead of executing the prompt and exiting.
- The `opencode serve` HTTP API works correctly: a session can be created and `POST /session/{id}/message` returns a response.
- `opencode run --attach http://127.0.0.1:4096` also hangs without output.

This means the planned `opencode run` invocation in `test-feature-builder.sh` cannot drive the agent headlessly. A working approach likely requires interacting with the `opencode serve` HTTP API directly (create session, send prompt, poll events/messages), but that is a larger change than this ticket originally scoped.

## Additional note

The requested model `opencode-go/deepseek-v4-flash` returns a region error unless explicitly opted in via `https://opencode.ai/workspace/wrk_01KKYT9XAMT2D4V2MPDJ2M8Q56/go`. `opencode-go/grok-4.5` works via the HTTP API and can be used for testing until DeepSeek is opted in.
