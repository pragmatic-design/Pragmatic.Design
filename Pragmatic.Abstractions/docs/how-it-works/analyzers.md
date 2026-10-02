# Analyzers — four mistakes the compiler will not let you make quietly

> **Scope**: `src/Pragmatic.Abstractions.Analyzers/` — 5 files, 452 lines: four `DiagnosticAnalyzer`
> classes (`InjectRequiredAnalyzer`, `CaptiveDependencyAnalyzer`, `BuildServiceProviderAnalyzer`,
> `JsonContextCoverageAnalyzer`) and the `DiagnosticDescriptors` that back them — plus
> `src/Pragmatic.Abstractions.CodeFixers/` — 1 file, 177 lines: `AddJsonSerializableCodeFixProvider`.
>
> **Not covered here**: the other analyzer projects in the repository —
> `Pragmatic.Persistence.Analyzers`, `Pragmatic.Result.Analyzers`,
> `Pragmatic.SourceGenerator.Analyzers`, `Pragmatic.Temporal.Analyzers`. They are separate rule sets
> with their own diagnostic ranges.

For the one-line catalogue, see [interfaces](../interfaces.md#26-analyzers-pragmaticabstractionsanalyzers).
This document is about what each rule actually looks for, and what happens to code that trips it.

## The inverse of everything else in Abstractions

The rest of this package is contracts: interfaces you implement, attributes you apply. These four
types are the opposite. Nothing calls them and nothing implements them — they are loaded by the C#
compiler and run *over your code*, once per compilation. The question is not "who consumes this",
it is "what does the compiler say when I get this wrong".

All four are pure diagnostics: they never change semantics, never fail a build on their own. In a
project with `TreatWarningsAsErrors` three of them do fail it, which is the point — the mistakes they
catch are the kind that compile perfectly and then misbehave in production.

Each analyzer declares `ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)`: generated
code is never analyzed, so nothing the source generator emits can raise one of these warnings at you.

## The four rules

| ID | Severity | Fires on | What it prevents |
|---|---|---|---|
| `PRAG1452` | Warning | A property or method carrying `[Inject]` without `Required = true` | A missing registration injected as `null`, surfacing later as a `NullReferenceException` at first use |
| `PRAG1450` | Warning | A constructor parameter of a `BackgroundService` / `IHostedService` typed `IOptionsSnapshot<T>` or a `DbContext` | A captive dependency: one scoped instance held for the life of the process |
| `PRAG1451` | Warning | Any call to `IServiceCollection.BuildServiceProvider()` | A second container, with duplicate singletons, twice-bound options and leaked disposables |
| `PRAG2800` | Info | A message, domain-event or job handler whose payload type has no `[JsonSerializable]` | Serialization that falls back to reflection and breaks under Native AOT |

### PRAG1452 — optional injection has to be said out loud

`[Inject].Required` defaults to `false`, and that default is deliberate: flipping it would break every
existing member. The consequence is that an unregistered service arrives as `null` instead of failing
at startup, so a misconfiguration survives the boot and shows up at the first call site.

The analyzer restores the fail-fast property without a breaking change. It reads the `Required` named
argument and stays silent **only** when it is the literal `true`. Everything else warns: the argument
absent, or written `Required = false`. Writing the `false` explicitly is not an escape — the rule is
that the optional, nullable contract must be acknowledged, and acknowledgement is a suppression, in
the file or in `.editorconfig`, not a keystroke that looks like intent.

The diagnostic is reported at the attribute itself when the attribute has syntax, otherwise at the
member. It applies equally to property injection and method injection, since both go through the same
attribute.

`[Inject]` also has to be in the compilation for any of this to happen: the analyzer resolves
`Pragmatic.Composition.Attributes.InjectAttribute` at compilation start and unregisters itself if the
type is not there.

### PRAG1450 — the captive dependency, in the two cases that are never right

`BackgroundService` and `IHostedService` implementations are registered as singletons. Anything
scoped they take in the constructor is captured once and kept for the lifetime of the application:
configuration that never refreshes, a `DbContext` shared across every unit of work and every thread.

Deciding in general whether a constructor parameter is scoped would require knowing the registrations,
which an analyzer does not have. So the rule is deliberately narrow and confines itself to two types
whose lifetime is not a matter of registration:

- **`IOptionsSnapshot<T>`** — scoped by definition. The message names `IOptionsMonitor<T>` as the
  replacement, which is the singleton-safe way to read live configuration.
- **Any type deriving from `DbContext`** — scoped by convention everywhere. The message names
  `IServiceScopeFactory` and a scope per unit of work.

The suggested fix is part of the message text, so the diagnostic tells you what to write, not only
what is wrong. The location is the offending constructor parameter. Abstract classes and non-classes
are skipped, and the whole rule switches off in a compilation that references neither
`BackgroundService` nor `IHostedService`.

### PRAG1451 — one container per application

`BuildServiceProvider()` called during configuration builds a throwaway container that is *not* the
one the host will use. Every singleton resolved from it is a different instance from the one the
application sees, options and configuration get bound a second time, and any `IDisposable` it creates
is never disposed. The code compiles, runs, and produces two of everything.

The match is on the invocation: method named `BuildServiceProvider` whose containing type is
`Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions` — the type that
holds every overload of the extension method, so the overload with `ServiceProviderOptions` is covered
by the same check. The diagnostic sits on the call expression.

This is the one rule with no activation gate: it registers an operation action directly, so it
applies to any compilation that loads the analyzer, whether or not the project references anything
else from Pragmatic.

### PRAG2800 — the payloads the generator could not cover

Under Native AOT, every type the framework serializes needs source-generated metadata in a
`JsonSerializerContext`. Message, domain-event and job payloads are serialized on their way to the
outbox, the transport and the job store, so a payload missing from the context is a runtime failure
that only appears in the AOT build.

The analyzer walks the handler interfaces — `Pragmatic.Messaging.IMessageHandler<T>`,
`Pragmatic.Events.IDomainEventHandler<T>`, `Pragmatic.Jobs.IJob<T>` — takes the payload type argument,
and checks it against the set of types covered by `[JsonSerializable]` anywhere in the assembly.

That set is built from the `JsonSerializerContext` types declared at namespace level in the assembly,
including the one the source generator emits. This is what keeps the rule quiet: the generator
already contributes `[JsonSerializable]` entries for the types it can handle, and what remains
flagged is what it could not reach. The walk descends into namespaces but not into types, so a
context nested inside another type is not part of the set — declare contexts at namespace level, as
the generated one is.

Positional records and init-only DTOs are **not** in that remainder — the generator covers both,
through `UnsafeAccessor`. What it cannot reach is a payload it never sees: a type that arrives only
at run time, or one whose shape is decided outside the compilation. Those entries have to be written
into a context of your own, because Roslyn generators are not chainable and one generator's output
cannot feed another's input.

Severity is `Info`, not `Warning`, because a project that has not committed to AOT should not be
nagged. The report location is the **handler type**, not the payload — chosen so the code fix has a
class declaration to start from.

## The code fix

`AddJsonSerializableCodeFixProvider` fixes `PRAG2800`, and only `PRAG2800`. From the flagged handler
it re-derives the payload type through the same three handler interfaces, finds a
`JsonSerializerContext` declared in the compilation, and offers:

> Add `[JsonSerializable(typeof(Payload))]` to `MyJsonContext`

Accepting it edits the context — a different file from the one the diagnostic is reported in — through
a `DocumentEditor`. The attribute is inserted fully qualified, then `Simplifier` reduces it to the
short form your `using` directives allow and `Formatter` lays it out to the project's style. A final
pass normalizes line endings to whatever the context file already uses, so the edit never leaves a
file with mixed CR/LF.

It supplies the batch Fix All provider, so a project that has just switched on source-generated
serialization can absorb every flagged payload in one action rather than one handler at a time.

## When the rules stay silent

Analyzers that run everywhere are noise, so each one (except `PRAG1451`) resolves its marker types at
compilation start and removes itself when they are absent. Worth knowing, because a rule that never
fires reads exactly like a rule that passed:

| Rule | Silent when |
|---|---|
| `PRAG1452` | `InjectAttribute` is not in the compilation |
| `PRAG1450` | Neither `BackgroundService` nor `IHostedService` is referenced — and always for scoped dependencies other than `IOptionsSnapshot<T>` and `DbContext` |
| `PRAG1451` | Never — no gate |
| `PRAG2800` | None of the three handler interfaces is referenced, or the assembly declares no `JsonSerializerContext` at all |

Two of those deserve to be spelled out.

**`PRAG1450` will not find your own scoped services.** A service you registered `Scoped` and injected
into a `BackgroundService` is a captive dependency too, and the analyzer says nothing about it. The
rule covers the two cases it can decide from the type alone; the rest is still yours to watch.

**`PRAG2800` requires that you have already opted in.** The gate is the presence of a
`JsonSerializerContext` in the assembly. An application on the reflection path gets no suggestions —
the first one arrives on the day it declares its first context, and then it arrives for every
uncovered payload at once.

## Suppressing PRAG1452

Suppress it at the member, with `#pragma warning disable PRAG1452` or a `[SuppressMessage]`, rather
than in `.editorconfig`: the rule is that an optional, nullable contract has to be acknowledged, and
an acknowledgement belongs where the decision was made rather than in a file nobody reads twice.

⚠️ Do not aim a `PRAG1647` suppression at optional injection: that id belongs to the Composition
generator, for something else entirely — `[Inject]` members being ignored on an open-generic service.
This rule is `PRAG1452`. Two rules under one id on the same build means one `.editorconfig` line
silences both, and the one silenced by accident is the one saying those members were dropped.
