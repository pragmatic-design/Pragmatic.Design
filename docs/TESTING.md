# Testing — how to get a green light that means something

The gate is **one script**, `scripts/check.mjs`, and CI runs exactly it. Anything else — a bare
`dotnet build`, a bare `dotnet test` over the solution — can report success on a repository that does
not build and whose tests do not pass. That is not hypothetical; it happened, and this page exists so
it does not happen again.

## Commands

```bash
node scripts/check.mjs --tier fast     # dev loop: build + hermetic tests of changed modules
node scripts/check.mjs --tier full     # clean build --warnaserror + ALL hermetic suites
node scripts/check.mjs --tier aot      # publish the AOT smokes Native AOT and run them
node scripts/check.mjs --tier docker   # the container suites, one at a time
node scripts/check.mjs --tier all      # full, then aot, then docker  ← before a commit series or a publish
node scripts/check.mjs --tier verify   # only: does every path CI names exist?
```

Options: `--jobs N` (hermetic parallelism, default cores−2), `--keep-going` (don't stop at the first
red suite), `--only <substring>` (run only the suites whose name contains it — **narrows the suites and
nothing else**: the clean build and every ratchet still run, and the run declares itself NOT the gate,
at the top and again at the end).

## Which tier, when

Measured on one `--tier all` run late in a working session (see the note on drift below), and the
reason there are two answers rather than one:

| tier | | |
|---|---|---|
| `--tier full` | **4m 04s** | **13 961** tests (0 skipped), 107 hermetic suites: the clean build, every hermetic suite, the samples, and the rest (ratchets, SBOM, vulnerability scan) |
| `--tier aot` | **1m 08s** | three Native AOT publishes |
| `--tier docker` | **16m 54s** inside `all` | **20** suites, **3 003** tests (0 skipped): TimeOff 4m 29s, Showcase 3m 25s, Casework 2m 53s, Warehouse 1m 32s, Mapping.EFCore 49s, Messaging 45s, Migrations 30s, Conformance 30s, Invoicing 27s, Privacy.E2E 20s, Persistence.EFCore 17s, the rest under 10s each |

The whole `--tier all` is **~22 min** on that run. The docker tier is about half a minute cheaper inside
`all` than standalone, where it pays its own clean build. Migrations is split across the two: its
hermetic tests live in `Pragmatic.Migrations.Core.Tests` (full tier), and `Pragmatic.Migrations.Tests`
holds the 74 that need a container.

⚠️ **Counts are trustworthy; timings are not — and the easy explanation is wrong.** Three `--tier all`
runs on the **same commit** gave docker **11m 56s → 14m 05s → 14m 33s**, full **2m 17s → 2m 35s →
3m 07s**, aot **57s → 1m 01s → 1m 23s**. Remeasured with nothing else running, it came out **slower
still**, so a busy machine does not explain it.

What settles it is the build: **583 warnings, identical** — the very same work — in **59.5s → 1m 08s →
1m 38s**. The machine drifts over a session, and stopping one's own commands does not undo it. So the
table's timings are an upper bound rather than a baseline: a late run is not comparable with a morning
one, and a delta is read between two consecutive runs rather than against this table.

**Per story: `--tier full`.** It keeps the half no suite can see — the clean build with
`--warnaserror`, and every ratchet — plus every hermetic test, which is what catches a source
generator regression.

⚠️ **What it does not cover is the container suites.** A story whose new test lives in one of them
(`Showcase.IntegrationTests`, `Invoicing.IntegrationTests`, …) has to run it:
`--tier docker --only <Suite>`. Invoicing alone is ~27s against ~17 min for the whole tier.

**Per batch: `--tier all`**, before a publish or a series of commits. Paying the whole thing for every single
story is what makes people abandon the gate for a bare `dotnet test` — which is the incremental-build
trap this script exists to close.

CI already splits it this way: `ci.yml` runs `--tier full` and `--tier docker` as two jobs.

⚠️ **Where the container tier's time goes is not where it looks.** Dividing a suite's wall clock by
its test count measures the scaffolding, not the tests: Migrations averages "445 ms per test" while its
358 tests execute in **15.7s**, 9% of the suite. The rest is whatever expensive resource is built once
per test — held as a field on the test class with `IAsyncLifetime`, which xUnit instantiates per test
method. A **container** held that way is started per test; Showcase's `IntegrationTestBase` holding a
whole **ASP.NET host** that way means building and disposing it 585 times at ~446 ms each. Sharing
them per class and per collection halves the tier (**10m 51s to 5m 28s**, same suites, same counts).

⚠️ **Two things keep `--tier full` short.** The smaller: the SBOM and the vulnerability scan answer
*unchanged* against a dependency graph that has not moved — so read the row as a **warm** repeat run,
and expect minutes more on the first run after a dependency change. The larger: the hermetic suites
run in parallel, so the tier's total is their makespan, not their **sum**: a run whose column sums to
755s finishes its suite phase in 40s.

⚠️ **A standalone `--tier docker` and the docker phase of `--tier all` are different runs**: `all`
reaches the container suites with the solution already built and the machine already warm. The
standalone tier prints its own `docker: <elapsed>` line so the two are not confused.

⚠️ **`--jobs` parallelises the hermetic tier and the container tier stays sequential**, and the
two are separate decisions. The runner yields (`scripts/lib/spawn-async.mjs`, tested in
`scripts/spawn-async.test.mjs`): with a synchronous `spawnSync` the worker pool's `async` workers would
never overlap, every tier total would be a sum, and the container tier's sequential-ness would come from
that defect rather than from its `parallel: false`. The container tier is sequential **on purpose**:
see the pruning note below.

### One run at a time

⚠️ **Two gate runs cannot overlap**, and the second is refused instead of allowed to try. Everything builds into a single `artifacts/build`, so a clean build in one run deletes what the
other is compiling: measured, a `--tier docker` started beside a `--tier full` failed with
*build --warnaserror … FAIL (390 error markers)* — wreckage shaped exactly like a code failure. The
refusal names the holder and how long it has held. A run killed with Ctrl-C leaves the lock behind; it
goes stale after 45 minutes rather than wedging the next run. `--tier verify` is exempt.

⚠️ A bare `dotnet test` beside a gate is a different matter: it writes elsewhere, so it does not
corrupt the build, but it competes for CPU and can leave test hosts behind. The gate sweeps orphaned
hosts at startup; it cannot sweep what is still running.

### Running the container suites in parallel: `--docker-jobs N`

Measured on a tier of **13 suites**, without the application examples, so the absolute numbers below
belong to that smaller tier and the ratio is what holds:

| | | |
|---|---|---|
| `--docker-jobs 1` (default) | **5m 32s** | one at a time, pruning between every suite |
| `--docker-jobs 2` | **4m 11s–4m 14s**, five consecutive green runs | −23%, 2421 passed each time |
| `--docker-jobs 3` | **4m 17s** | no better than 2 |

**The default stays 1**, and the knob is there so the choice is a flag rather than an argument. Three
is not better than two because the suites run in **waves**: a wave cannot start until the previous one
drains, since pruning stopped containers and unused volumes is what keeps the daemon healthy across a
long tier and it cannot run beside a suite that is starting one — an unused volume is also a volume
nobody has attached *yet*. `Showcase.IntegrationTests` at 1m47 is alone on its wave's critical path,
so widening the wave adds idle workers, not throughput.

⚠️ The lever that would matter is **ordering**, not width: start the long suites first and let the
short ones fill in around them, and the floor is Showcase plus a tail rather than a sum of wave
maxima. That needs either an open pool — which gives up the prune cadence, and the daemon degrading
after about seven suites is exactly what that cadence exists to prevent — or durations remembered
between runs. Neither is done.

⚠️ **Shared does not mean shared all the way down.** The container is per class and the **database is
per test**, because the migration scenarios diff a desired schema against the real database and a
neighbour's tables would enter that diff. The host is per collection and the **DI root** comes with
it, so singletons now live for the run: a class that needs cold ones overrides
`IntegrationTestBase.NeedsItsOwnHost` and says why.

**To find the next one**: run the suite with `-v n`, sum the `[N ms]` column, and subtract it from the
wall clock. What is left is the scaffolding.

## Three ratchets on the AOT surface

`--tier full` runs all three, after the clean build:

**`generated code serializes without reflection`** reads every `.g.cs` in the build output and fails on
an untyped `ReadFromJsonAsync<T>` or `Deserialize<T>`. Such a call site is invisible at run time,
because a reflection fallback answers for it.

**`trim/AOT warnings from Pragmatic assemblies`** counts IL2026/IL3050 attributable to `Pragmatic.*`
projects against a budget that may fall and never rise. It is not zero and will not be: EF Core is out
of scope, assembly scanning is incompatible with trimming by construction, and a logging provider that
renders arbitrary objects is reflective by definition. What it prevents is a new untyped serializer
call, or a new endpoint mapped with a handler `Delegate`, arriving unnoticed.

**`reflection in runtime source`** counts reflection calls in the `src/` of every runtime module —
tests, generators, analyzers and `Migrations.Cli` excluded — against a second budget,
`REFLECTION_BUDGET`.

It exists because the trim ratchet above provably cannot see most of the reflection in this repo.
IL2026/IL3050 measure trim-safety as the linker sees it, and `GetProperty` on a type the caller already
named, `GetCustomAttribute` and `GetInterfaces` produce no warning at all: a module can report **zero**
trim warnings and still contain reflection. Two different properties, and each needs its own gate.

It is source-level and deliberately crude: it counts call sites, not reachability. A site that is
annotated and unreachable by default counts the same. Lowering it means deleting the call.

A member lookup counts only when the line also carries `BindingFlags` or a `typeof(...)`, and comments
and string literals are stripped first. Without that the count is inflated by comments that name an API to say it is *not* used, by our own `LogContext.GetProperty`, and by EF Core's
`entityType.GetProperties()` — none of which is reflection.

Lower either budget when its count comes down. Never raise one to make a build pass — that turns the
signal that measures the trend into a record of having given up on it.

## The ratchet on what a package declares about its own contracts

**`a contract a package registers says who registers it`** runs before the build. It counts the
interfaces this repository declares that a Pragmatic package registers — `services.AddScoped<IFoo,
Foo>()` and its variants, anywhere in a `src` tree — and whose own file carries no `[ProvidedByHost]`.
102 at the initial measurement, of 117 registered.

It exists because that attribute is what tells a module's generator who registers a contract, and
**nothing failed when a package forgot it**: the build here stayed green and the failure landed on the
first application that injected the contract from a `[Service]`, as PRAG1641 on code that is correct.
Four contracts were in exactly that state when the mechanism was finished — `IFileStorage`,
`IEmailSender`, `ITenantStore` and `IStringLocalizer`.

⚠️ It is a population to triage, not a bug list: a contract only the framework resolves is registered
the same way and counted the same. What makes a reported one real is whether an application's own
`[Service]` would ever take it in its constructor. And it reads by **name**, so what it can say about
a name two packages declare is nothing — those are dropped rather than guessed at.

## The ratchet on attributes nothing reads

**`attributes nothing reads`** counts the public attribute types the framework declares that nothing
reads except a check on how they are written, and — for the types something *does* read — the settable
properties nothing names. **3 and 0** at the initial measurement, of 295 declared attributes.
`node scripts/attribute-readers.mjs` prints the names.

It exists because `[MessageMiddleware]` registered nothing. Its only reader was a shape diagnostic
verifying the class implements the interface, so a middleware declared that way compiled, passed its
own check, and the pipeline never called it — and there is **nothing to observe from outside**, because
the messages are still handled, just not wrapped. `ForMessageType` was the same defect one
level down: declared, documented, read by nobody, while the type around it was read, so a type-level
count reported it clean.

"Read" is evidence in the source rather than a list somebody keeps: the token `FooAttribute` outside
the file that declares it, or an index constant (`AttributeNames.Foo`) used somewhere, or an override
of an attribute base class the framework reads. Anything inside a `RegisterShapeDiagnostic(…)` call is
not a read, which is the whole shape being counted.

⚠️ **It does not catch a declaration that is read but never reaches what runs.**
`[FromBusinessTimezone]` was read the whole time — by the model binder at run time and by the Temporal
feature in the generator — and the defect was that the declaration never reached the generated
`{Trigger}Body` record, the type the endpoint deserializes. *Is it read* and *does it reach what runs*
are two questions; only the first is countable here. The second is what an example plus an assertion
answers, which is the coverage measure above.

⚠️ It undercounts on purpose: a token match says something *names* this, not that something *acts* on
it, so every attribute it reports is genuinely unread while some read-only-in-a-dead-branch are not
reported. And a property named like a common word — `Order`, `Name` — reads as read. Both are the cost
of not keeping an exclusion list. Five false readings were corrected out of the measure before its
number went into a budget, two of which read **low**; each is a case in
`scripts/attribute-readers.test.mjs`.

## The AOT tier

`--tier aot` publishes three samples Native AOT and runs the binaries. It is the only signal that
answers *does this work when published*: a solution can compile clean with every test green while a
generated endpoint published AOT answers **500** — or **200 with an empty body**, which is worse,
because it looks like success. Left as manual smokes, that goes unnoticed.

It needs the native toolchain (Windows: a Visual Studio C++ workload). When it is missing the tier
**fails** rather than skipping: a skip that reads as a pass is the failure mode this tier exists to
prevent. See `examples/aot-smoke/README.md` for what each sample covers.


**`fast` is not the gate.** It builds incrementally, on purpose, to stay quick. Use `all` before you
commit.

## The three traps this replaces

**1. An incremental build hides errors.** MSBuild does not re-evaluate projects it considers
up to date, so `--warnaserror` only inspects what happened to be rebuilt. The same sources have
reported **0 errors** incrementally and **14** clean. Every gate tier builds with
`--no-incremental`.

The gate also pins the version (`-p:MinVerVersionOverride`). MinVer shells out to git once per project,
and across 150+ projects one invocation sporadically fails with `MINVER1007: git is not present in PATH`
— failing the build for a reason that has nothing to do with the code. The gate compiles and tests, it
never packs, and snapshot tests already scrub the version, so a fixed value costs nothing.

Corollary for measuring a baseline: compare **clean against clean**. Comparing a clean build (with your
changes) to an incremental one (without) makes pre-existing errors look like your regression — which is
exactly the wrong turn that cost a diagnostic round.

**2. Container suites in parallel exhaust Docker.** Eleven test projects start Testcontainers. Run
together they saturate the daemon; tests then fail on container start-up, and the failure reads like a
code regression. Once, 26 tests failed this way while every single suite passed in isolation. On a
developer machine it can take the whole host down.

Those projects declare it themselves:

```xml
<PropertyGroup>
  <RequiresDocker>true</RequiresDocker>
</PropertyGroup>
```

`check.mjs` reads that property: marked projects run **sequentially**, everything else in parallel.
**If you add a Testcontainers fixture to a test project, add this property too** — otherwise the
project rejoins the parallel pool and the saturation comes back. A list kept inside the script would
have drifted the first time someone forgot; a property next to the project is visible when you open it.

**2-bis. Serialising is not enough — the daemon also accumulates.** With the suites running one at a
time, Docker still degraded after about seven of them: the last four suites reported failures while
*every hermetic test inside them passed*, and afterwards the daemon stopped answering `docker version`
altogether. Testcontainers removes its containers via Ryuk, but not always promptly, and the leftovers
add up. The `docker` tier prunes stopped containers and unused volumes between suites, and — if a
suite fails — checks whether Docker is still answering before blaming the code. Measured with eleven
suites: with the prune the tier reaches the end of all of them (1,991 passed, 0 red); without it, it
gives out around the seventh.

Even so, those eleven suites are hard on the daemon — it had to be restarted twice while this was being
worked out. If it stops answering, no amount of retrying inside the gate will help; restart Docker.

How to tell the difference yourself, without Docker: count the tests that need a container and compare.
When the daemon died, the failures matched that set *exactly* — `Persistence.EFCore` 22 failed against
11+11 container tests, `Migrations` 38 against 19 inherited scenario tests × 2 providers,
`Messaging` 2 against 2. Not one hermetic test fell over. A real regression has no reason to hit every
container test and spare the 625 hermetic ones in the same assembly.

**3. A CI job can be dead without anyone noticing.** Two jobs referenced solution files that do not
exist (`Pragmatic.Showcase/Pragmatic.Showcase.slnx`, `Pragmatic.Persistence.EFCore/…`) and were gated
on `if: github.event_name == 'pull_request'`. With a trunk-based flow there are no pull requests, so
they never ran and never failed. `--tier verify` fails when CI names a path the repo lacks — including
paths assembled from a matrix, since skipping those is precisely how such a job hides.

## What a red run leaves behind

Every red run writes `artifacts/gate/failures-<instant>.json` — **one file per red run**, not one that
is overwritten, because a flake is a comparison between runs and a "last failure" file can only answer
about the most recent one. The last 20 are kept.

Each failed suite carries two things per test, and they are not alternatives:

- `tests` — the same capped line the terminal printed. It stays capped: a failing full run prints up to
  ten of them, and a wall of frames is how the names stop being read.
- `details` — the runner's own account: the whole message and the stack, with the file and line of each
  frame, from the test's outcome line to the next test's, capped at 40 lines.

⚠️ With only the first, the record would hold exactly as much as the terminal does — which is the
thing it exists to outlive. The capped line is the test's name and the start of its
failure message; when the failure is an exception thrown by something the test called, the cause is
in the stack, below the message. Re-running a slow container suite to see that frame costs minutes
each time; the record already has it.

⚠️ The capture is by **position** and never by label. "Non superato" here is "Failed" in CI, and the
labels inside a block ("Messaggio di errore", "Analisi dello stack") are translated too. What
identifies a test's outcome line is the duration in brackets at its end — and the detail blocks are
**interleaved** with the passing tests, so "until the next blank line" would have kept nothing. The
cases in `scripts/failure-record.test.mjs` are driven by `scripts/fixtures/dotnet-test-red-it-IT.txt`,
a real run's captured output, because a shape checked only against invented strings can match
nothing on a real run and still pass.

## What "green" requires

- `dotnet build --warnaserror --no-incremental` over the solution: **0 errors**.
- Every hermetic suite green.
- Every sample project runs to exit 0 (`scripts/run-samples.mjs`, from the assemblies the clean build
  produced). A sample that cannot run here — a web host that serves until stopped, a consumer sample
  built against the published packages — is named with its reason in `scripts/samples-excluded.json`;
  an exclusion without a reason, or for a project that no longer exists, fails the run.
- Every container suite green, run one at a time.
- Docker unreachable is **not** a pass: the container tier reports failure rather than skipping quietly.
- No known vulnerability in any dependency, direct or transitive.
- A supply-chain scan that could not run is **not** a pass either — offline, a restore failure and a
  NuGet outage all return zero findings, which is exactly what a clean repo returns.
- Every tracked `.cs` under a `tests/` folder belongs to a project. ⚠️ The suites are discovered by
  walking for `*Tests.csproj`, so a folder of tests with **no** project file is not a suite that is
  missing from the solution — it is not a suite at all, and nothing else in the gate can see it. One
  such folder held two tests that had never run, and a repository-wide rename had edited them
. The check looks at the files instead of the projects and fails naming each one.

## Supply chain

`--tier full` and `--tier all` also run `scripts/supply-chain.mjs`, which uses the SDK's own
`dotnet list package` — no extra tooling — to:

- write a **CycloneDX 1.6 SBOM per shipped package** plus an aggregate, into `artifacts/sbom/`
  (gitignored). The serial number is derived from the component set and no timestamp is emitted, so two
  SBOMs differ only when the dependencies did;
- **fail on any known vulnerability**, naming package, severity, project and advisory URL.

Verified by pointing it at a project pinned to `Newtonsoft.Json` 12.0.3 (GHSA-5crp-9r3c-p9vr): the check
fails and names it. A clean result means nothing was found, not that nothing looked.

The scan's result is ANDed with the test run rather than short-circuiting it, so one bad dependency does
not hide whatever the suites would have said.

**Cost, and what it implies.** Cold, the step is **~190 seconds**, most of it querying the vulnerability database for 313 projects. It does not run in
`--tier fast`, which is the dev loop.

**It is cached on the dependency graph.** Both halves answer a question about the graph, so an unchanged
graph has an unchanged answer. The key is a SHA-256 over the *content* of `Directory.Packages.props`,
`Directory.Build.props`/`.targets`, `NuGet.config`, `global.json` and **every `.csproj` in the tree**;
the cache is `artifacts/supply-chain.cache.json`, which is gitignored, so **CI is always cold**.
Measured: 189.5s cold, **0.2s** warm, and back to 199s the moment one byte of `Directory.Packages.props`
changes.

Two asymmetries make it a cache and not a way of not looking:

- **only a green result is ever cached.** A run that found a vulnerability, or that could not run at
  all, is repeated every time. Reusing "it was fine an hour ago" is how a gate starts answering from
  memory instead of from the world.
- **the SBOM is keyed on content alone; the scan is keyed on content AND age.** The SBOM is a pure
  function of the graph. The scan is not — the advisory database moves under a graph that has not — so
  a clean scan is reused for at most **12 hours** (`SCAN_MAX_AGE_MS`). The SBOM half is additionally
  skipped only while the documents it would have written are still in `artifacts/sbom/`.

**The line says which of the two it is**, because a reused verdict and a fresh one are not the same
news:

```
  known vulnerabilities … scanned  none
  known vulnerabilities … reused   none, from a scan 30 min ago on the same dependency graph — rescans after 12h
```

Twelve hours is a working day: a session pays one scan and reuses it until it ends, and the next day
pays another. The rule is measured — `scripts/supply-chain.test.mjs`, which the gate runs as
**`a clean scan stops being reusable`** — so deleting the age check fails a test instead of passing
unnoticed.

⚠️ **The window it bounds is local.** CI is always cold, and `ci.yml` triggers on push to `main` and
on pull requests only: there is no schedule. So a repository nobody touches is not scanned at all,
whatever this constant says, and the first push after a quiet week is when an advisory published
during it is heard. Closing that needs a scheduled workflow, not a shorter cache.

To force a full pass, delete `artifacts/supply-chain.cache.json`. There is deliberately no flag: a
switch that turns off a security check gets used routinely, and then the check exists only in the belief
that it runs. What the cache removes is the repetition, not the check.

The consequence worth knowing before it surprises you: **`--tier full` and `--tier all` need network
access** on any run where the graph changed or the scan aged out. Offline with an unchanged graph the
cached answer stands; offline with a changed one, the gate says it could not run, which is not a pass.

## Notes

- Some suites are load-sensitive without needing Docker (`Pragmatic.Jobs.Tests` uses lease-based
  locking and can miss timings under a fully loaded machine). If a suite is red in a parallel run,
  re-run it alone before concluding anything.
- A flaky suite is not proven fixed by one green run. The outbox race in `Pragmatic.Messaging.Tests`
  appeared roughly once in three; it took five consecutive green runs to be reasonably confident, and
  that is evidence, not proof.
- `JobLeaseIntegrationTests.RecurringStore_TryClaimDueAsync_OnlyOneHostWinsATick` is **not
  reproducible** as flaky: **20/20 green run alone, plus five `--tier all` runs where its suite passed
  510/510** — 25 observations, no red. It is 8 tasks racing one compare-and-swap, so it is
  load-sensitive exactly like the note above. Not reproducible is a different claim from fixed, and
  calling it flaky needs a red run to point at, not a memory of one.
