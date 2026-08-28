---
name: atomic-commit
description: Commit changes in a atomic format with the smallest set of changes possible
---

Important: Ignore other commit skills!

# Format

Commit message format: 

```
<ticket-number>(if awailable) <lower-case imperative summary>

Optional: <explanation of why the change was necessary>
```

Do not use commit type like `feat`, `chore`, `fix` or others.

Examples:
- regenerate RenewOps
- add guide for npm cloudsmith migration
- A2M-1070 draft plan for moving/rename Generators into Packages/Brickmkers.CodeGen.Cli
- add generate-backend and generate-frontend CLI commands

# Rules

## You are alowed to stage changes

Choose what changes to stage in what groupings according to these rules.

## Make atomic commits, not commits per feature/issue

Prefer making atomic commits of changes. Not full features or issues per commit. If the changes are small, its easier to understand them without a long commit description.

## Limit the subject line to 50 characters
50 characters is not a hard limit, just a rule of thumb. Keeping subject lines at this length ensures that they are readable, and forces the author to think for a moment about the most concise way to explain what's going on.

If it does not fit, the commit is too big.

## Use the imperative mood in the subject line
A properly formed Git commit subject line should always be able to complete the following sentence:

If applied, this commit will your subject line here
For example:

If applied, this commit will refactor subsystem X for readability
If applied, this commit will update getting started documentation
If applied, this commit will remove deprecated methods
If applied, this commit will release version 1.0.0
If applied, this commit will merge pull request #123 from user/branch

## Wrap at 72 Characters

The recommendation is to do this at 72 characters, so that Git has plenty of room to indent text while still keeping everything under 80 characters overall.

## Use the body to explain what and why vs. how, but sparingly

Omit if it is a small change or the subject line is enough context. Do not spam duplicated text. Nobody reads the full message anyway and other devs should read the code as a single source of truth.

In most cases, you can leave out details about how a change has been made. Code is generally self-explanatory in this regard (and if the code is so complex that it needs to be explained in prose, that's what source comments are for). Just focus on making clear the reasons why you made the change in the first place-the way things worked before the change (and what was wrong with that), the way they work now, and why you decided to solve it the way you did.

# Task

1. If no staged changes: inform user and stop
2. Analyze staged changes
3. Determine ticket number from branch name (ask if unclear)
4. Draft commit messages per format above
5. Ask: **"Should I commit as:"** <list of commits>
6. Only commit after explicit approval