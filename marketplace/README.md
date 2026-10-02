# Pragmatic.Design — agent skills

Skills that teach a coding agent to build .NET line-of-business applications with the
[Pragmatic.Design](https://github.com/pragmatic-design/Pragmatic.Design) packages: which packages to
reference, how to lay out the solution, the attributes of each module, the traps, and how to verify the
result. They are written for a **consumer project** — an app that uses the `Pragmatic.*` packages — and
need no access to the framework's source.

Each skill is a folder with a `SKILL.md` in the [Agent Skills](https://agentskills.io/specification)
format, so the same folders work in Claude Code, in Codex and in any agent that reads that format.

## Install

### Claude Code

```bash
claude plugin marketplace add pragmatic-design/skills
claude plugin install pragmatic-design@pragmatic-design
```

Skills are then called by the agent when a task needs them, or by name:
`/pragmatic-design:pragmatic-new-app`.

### Codex

```bash
codex plugin marketplace add pragmatic-design/skills
codex plugin add pragmatic-design@pragmatic-design
```

Start a new session after installing. `$` followed by a skill name calls one explicitly.

### Any other agent

Copy the folders under `plugins/pragmatic-design/skills/` to where your agent loads skills from — for
example `.agents/skills/` (Codex, per project), `~/.agents/skills/` (Codex, every project) or
`.claude/skills/` (Claude Code, per project). The folders need no change, but copy them all: the
module skills link to references kept in `pragmatic-ecosystem`.

## Where to start

| Skill | For |
|---|---|
| `pragmatic-new-app` | Scaffolding a new application |
| `pragmatic-architecture` | Splitting an application into libraries, modules and databases |
| `pragmatic-choose-modules` | Choosing the packages for a feature |
| `pragmatic-ecosystem` | The reference behind the others: packages, patterns, diagnostics, recipes |
| `pragmatic-nuget-feed` | The local NuGet feed the alpha packages come from |

Then one skill per module, `pragmatic-use-*`, loaded when a task reaches it: actions and endpoints,
persistence, composition, identity, authorization, delegation, multi-tenancy, events, messaging, jobs,
caching, configuration, feature flags, resilience, logging, temporal, i18n, storage, documents, email,
notifications, imaging, audit, privacy, migrations, testing, client, traits, distributed, and the
foundation libraries. The folder list under `plugins/pragmatic-design/skills/` is the complete set.

## Updates

The plugin's `version` is in `plugins/pragmatic-design/.claude-plugin/plugin.json` and in the
marketplace entry, and changes with every release of the skills: an installed copy updates only when it
does. In Claude Code run `claude plugin update pragmatic-design@pragmatic-design`, or turn on auto-update
for the marketplace in `/plugin`.

## Local development

From a checkout, without installing anything (Claude Code):

```bash
claude --plugin-dir ./plugins/pragmatic-design
```

Or register the checkout as a marketplace — `claude plugin marketplace add .` or
`codex plugin marketplace add .` from this directory — and install as above.
