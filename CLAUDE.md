# CLAUDE.md

Instructions for Claude Code in this repository. The build, the layout and the invariants are in
AGENTS.md, which applies to every agent:

@AGENTS.md

## Language

Everything that lands in the repository is in English: code, identifiers, comments, documentation,
commit messages, pull request titles and descriptions.

## Commits

The history of this repository is public. A commit message follows CONTRIBUTING.md ("Commit
messages"), and `scripts/commit-message-check.mjs` enforces it — in the `commit-msg` hook
(`git config core.hooksPath .githooks`) and on every pull request:

- a Conventional Commits header, lowercase description, at most 72 characters;
- a body that says why the change was made, not what the diff already shows;
- no trailers that link a private session, and no co-author line for an assistant;
- no issue-tracker keys, review tags or references to documents outside the repository;
- no paths from a local machine.

## Before you call something done

Run the gate the change needs (`node scripts/check.mjs --tier full`, or `--tier all` for a change
that reaches the container suites) and read its result. A change is done when the gate says GREEN,
not when it compiles.
