---
name: implement
description: "Implement a piece of work based on a spec or set of tickets."
disable-model-invocation: true
---

Implement the work described by the user in the spec or tickets.

Use /tdd where possible, at pre-agreed seams.

Run typechecking regularly, single test files regularly, and the full test suite once at the end. If the caller says a separate phase runs the tests (or otherwise asks to skip the full run), skip the full test-suite run at the end.

Once done, use /code-review to review the work.

Commit your work to the current branch.
