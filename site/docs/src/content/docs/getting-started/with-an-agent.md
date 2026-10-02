---
title: Build with an agent
description: Install the Pragmatic skills in Claude Code, Codex or any agent that reads SKILL.md, and start an application from a prompt.
---

A coding agent can build a Pragmatic application from a description, and it does better than with most
frameworks: a declaration is a dozen typed lines it can read, and a `PRAG` diagnostic is a loop it can
close on its own. What it lacks is knowledge of the framework. The **Pragmatic skills** give it that — 35
folders, one per topic, that an agent loads when a task needs them: which packages to reference, how to
lay out the solution, the attributes of each module, the traps, and how to verify the result.

Writing the code yourself? Start at [Installation](/getting-started/installation/) instead.

## What an agent needs

- **The skills.** They are written for a project that consumes the packages, without the framework's
  source at hand. Installing them is the one step below.
- **The packages.** During the alpha they come from a local NuGet feed; the `pragmatic-nuget-feed` skill
  sets it up.
- **Optionally, the index.** [`llms.txt`](/llms.txt) lists every page of this site for an agent that
  fetches documentation.

## Install the skills

The skills ship as one plugin, `pragmatic-design`, in the
[`pragmatic-design/skills`](https://github.com/pragmatic-design/skills) repository.

### Claude Code

```bash
claude plugin marketplace add pragmatic-design/skills
claude plugin install pragmatic-design@pragmatic-design
```

In a session, `/plugin` shows the plugin, and a skill can be called by name, for example
`/pragmatic-design:pragmatic-new-app`. Updates arrive when the plugin's version changes: run
`claude plugin update pragmatic-design@pragmatic-design`, or turn on auto-update for the marketplace in
`/plugin`.

### Codex

```bash
codex plugin marketplace add pragmatic-design/skills
codex plugin add pragmatic-design@pragmatic-design
```

Start a new session after installing. In a session, `/plugins` shows the plugin, and `$` followed by a
skill name calls it explicitly; otherwise Codex picks a skill by its description.

### Any other agent

Each skill is a folder with a `SKILL.md`, in the [Agent Skills](https://agentskills.io/specification)
format, under `plugins/pragmatic-design/skills/` in the repository. Copy the folders where your agent
looks for skills — for Codex without the plugin, `.agents/skills/` in the project or `~/.agents/skills/`
for every project; for Claude Code without the plugin, `.claude/skills/` or `~/.claude/skills/`. For
another agent, check where it loads skills from. The folders need no change, but copy them all: the
module skills link to references kept in `pragmatic-ecosystem`.

## Where to start

The agent chooses skills by their description, so a plain request is enough. Three are entry points:

| Skill | For |
|---|---|
| `pragmatic-new-app` | Scaffolding a new application: projects, packages, host, first entity |
| `pragmatic-architecture` | Deciding how to split an application before writing it |
| `pragmatic-ecosystem` | The reference behind the others: packages, patterns, diagnostics, recipes |

The rest are one per module — `pragmatic-use-persistence`, `pragmatic-use-actions-endpoints`,
`pragmatic-use-identity`, and so on — and load when the task reaches that module.

## A first request

> Create a Pragmatic.Design application for managing a small library: books, members, loans with a due
> date. Expose it over HTTP, store it in PostgreSQL, and let only librarians register loans.

A good session then goes through the same steps you would: it sets up the feed, creates the host and a
boundary library, declares the entities and the operations, builds, and fixes what the generator reports.
Ask it to show the generated code under `obj/` when you want to see what the declarations turned into.

## Next steps

- [Installation](/getting-started/installation/) — the same path, by hand
- [Architecture](/getting-started/architecture/) — the model the agent follows
- [Diagnostics](/reference/diagnostics/) — what each `PRAG` code means
