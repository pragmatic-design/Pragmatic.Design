# AGENTS.md

Entry point for coding agents. Humans should start at [README.md](README.md) and
[CONTRIBUTING.md](CONTRIBUTING.md); everything here is also true for them.

## What this is

A .NET 10 framework built as a monorepo of independent modules, held together by one source
generator. Roughly 350 projects, 158 of which ship as packages (what `scripts/publish-local.mjs`
pushes to the feed). Solutions use the `.slnx` format, not `.sln`.

## Build and test

One script defines both, and CI runs the same one:

```bash
node scripts/check.mjs --tier all    # clean build + every suite — this is the gate
node scripts/check.mjs --tier fast   # changed modules only, while you work
```

**Do not substitute `dotnet build` or `dotnet test` across the solution.** They mislead in opposite
directions: an incremental build reports success on sources that fail when compiled clean, and
`dotnet test` on the solution starts the Testcontainers suites concurrently, saturating Docker and
failing tests that pass alone. [docs/TESTING.md](docs/TESTING.md) has the detail.

To run one suite: `dotnet test <module>/tests/<project>/<project>.csproj --filter "..."`.

## The five things most likely to be got wrong

1. **Generated code comes from a template class, never a `StringBuilder`.** Every generator output
   is a class under `Templates/` deriving from `CSharpTemplate`, fed by an immutable model. Ad-hoc
   string building is rejected on review.
2. **The generator compiles as `netstandard2.0`; what it emits runs on `net10.0`.** Language
   features work in both, BCL types do not — `System.Threading.Lock`, `params ReadOnlySpan<T>` and
   inline arrays are unavailable in generator code.
3. **Failures are values.** Business operations return `Result<T, TError>`; exceptions are for
   programmer error. Guards use `Ensure.ThrowIf*`.
4. **The generated path never reflects.** `GetProperty`, `MakeGenericType`,
   `Activator.CreateInstance` and friends are out of the code the generator emits and out of the code
   that consumes it: the shape is known at compile time, so it is typed. This is a rule about what you
   may write, not a property the whole runtime already has — there are reflective fallbacks for
   "the generator registered nothing", they are annotated with `[RequiresUnreferencedCode]`, and the
   `reflection in runtime source` ratchet in `scripts/check.mjs` counts every one of them against
   `REFLECTION_BUDGET` so the number can fall and not rise. EF Core's internals are out of scope.
5. **A file holds one type**, and the namespace follows the directory.

## Where things live

| | |
|---|---|
| What each status means, and what moves before 1.0 | `docs/ROADMAP.md` |
| The source generator, and its contributor docs | `Pragmatic.SourceGenerator/`, `…/docs/` |
| Code shared into generators | `shared/SourceGen/` |
| End-to-end example over a real database | `examples/showcase/` |
| Reference applications, each with its own README | `examples/time-off/` (one module, local accounts), `examples/invoicing/` (two modules, multi-tenant, OIDC), `examples/casework/` (two services over a broker, a database per tenant, documents from templates), `examples/warehouse/` (three services behind a gateway, one of them twice, an Agent per host) |
| Build configuration | `Directory.Build.props`, `Directory.Build.targets` |

Each module is `Pragmatic.{Name}/` with `src/`, `tests/`, and its own `.slnx`.

## Conventions worth knowing before your first change

- Attributes are generic: `[MapFrom<Order>]`, never `[MapFrom(typeof(Order))]`.
- Logging goes through `[LoggerMessage]`, never an interpolated string.
- Diagnostic IDs are `PRAG####`, one range per module — check the whole repository before claiming a
  new one, since analyzers outside the generator emit some of them.
- History is linear: rebase, never merge.

The complete standard, including the parts that only matter once you are deep in a module, is
[docs/CONVENTIONS.md](docs/CONVENTIONS.md).

## How a change gets accepted

Agent-written contributions are welcome. What follows is not etiquette — it is what makes one
reviewable, and it is the same bar the maintainers hold themselves to.

**Know what done means before you start.** Name the thing that will show the change worked: a test,
an exit code, a diagnostic that now fires. If no such thing exists, building it is the first commit.
"It should work" closes nothing.

**Verified means you ran it.** Show the command and its output. A change that was not exercised is
not finished — say so plainly rather than implying otherwise. Note that the absence of an error is
not evidence of success: check that the step did what it was for, not merely that it failed to
fail.

**Check instead of assuming.** If confirming something costs one command — a path, a config key, a
signature, what a document actually says — run it. Read a symbol where it is defined the first time
you touch it, not from memory. One occurrence is a hypothesis; look at a second before treating it
as the convention.

**Fix the cause.** If the correct fix is larger than the change you set out to make, say so and
propose it; do not quietly ship the workaround as though it were the repair. A mitigation described
as a fix is worse than an open bug, because it stops being looked for.

**Never invent.** Not an API, a signature, an option, a file's contents, a command's output, or a
test result. If you did not read it or run it, do not assert it.

**Say what you left out.** A change that covers part of what was asked is fine when the rest is
stated. One that quietly narrows the scope is not — the reviewer cannot see the gap.

**These need an explicit reason in the pull request**, because each one makes a red signal green
without fixing anything: an empty `catch`; a cast to `object` to satisfy the compiler; a disabled
test or a weakened assertion; `#pragma warning disable`; `--no-verify`; a sleep-and-retry standing
in for real synchronisation; a hardcoded value routing around a broken integration.

**Do not bring in a new dependency** without raising it first, and keep the change to what was
asked — no reformatting, no import reordering, no renames in code you were not sent to touch.

**If three attempts at the same problem fail, stop.** Report what you tried, what you observed, and
what you would need. A fourth variation is not a strategy.
