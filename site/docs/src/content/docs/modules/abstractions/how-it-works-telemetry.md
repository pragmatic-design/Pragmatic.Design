---
title: "Telemetry — one vocabulary, and nothing to pay when nobody listens"
description: "> **Scope**: `src/Pragmatic.Abstractions/Telemetry/` — 11 files."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Abstractions/docs/how-it-works/telemetry.md
sidebar:
  order: 23
---
> **Scope**: `src/Pragmatic.Abstractions/Telemetry/` — 11 files.
> `ActivityHelper` · `TelemetryOptions` · and the nine convention classes under
> `Telemetry/Conventions/`: `ActionTags` · `CacheTags` · `DbTags` · `ErrorTags` · `EventTags` ·
> `I18NTags` · `JobTags` · `MessagingTags` · `ResilienceTags`.
>
> **Not covered here**: what the host actually registers with OpenTelemetry lives in
> `Pragmatic.Composition.Host` (`PragmaticTelemetry`), and each module's own instrumentation lives
> with that module. Both are named below, not opened.

For the constant-by-constant catalogue, see [interfaces](/modules/abstractions/interfaces/#17-telemetry). This
document is about how the pieces fit together.

## The problem it solves

Pragmatic does not own a tracing API. Modules instrument with `System.Diagnostics.Activity`, the
BCL type that OpenTelemetry consumes directly. What Abstractions owns is the two things that hurt
when every module invents them separately: **what a tag is called**, and **what an instrumented
call costs when no exporter is attached**.

Everything here is `const string`, four extension members, and `TelemetryOptions` — the one type
with state, holding what the host configures. There is no interface to implement.

## `ActivityHelper` — the null guard belongs in the helper

`Activity.Current` is `null` whenever no listener is subscribed, which in a normal test run or a
production process with tracing disabled means *always*. Written by hand, every instrumented call
site grows the same shape:

```csharp
if (activity is not null)
{
    activity.SetTag(...);
    activity.AddEvent(...);
}
```

The four members — `RecordException`, `SetSuccess`, `SetFailure`, `AddNamedEvent` — are extension
members on `Activity?`, and each opens with a null guard before touching anything. The guard is the
first statement, so no tag collection is built and then discarded when there is no activity. Each
returns the activity, so calls chain.

One caveat on `AddNamedEvent`: its tags are a `params` array, and the caller materialises that array
*before* the method runs, so the guard cannot prevent it. Passing tags allocates whether or not an
activity is listening; passing none does not.

The guard is what lets a call site stay a single statement instead of an `if`. `RecordException` is
what an exception path looks like across the framework — Actions (`ActionInvokerBase`,
`MutationInvoker`), Events (`InMemoryEventDispatcher`), Messaging (`InMemoryMessageBus`),
Persistence.EFCore (`BulkExecutor`, `EfCoreQueryExecutor`, `EfCoreUnitOfWork`) and Resilience
(`ResiliencePipeline`) all call it, and all eleven call sites write `activity?.RecordException(ex)`.
The null-conditional already short-circuits them, so inside the framework the guard is never the
thing that fires; it is there for a consumer that holds a non-nullable reference, and for the case
where a future call site drops the `?.`.

`RecordException` follows the OpenTelemetry exception convention: it attaches an `ActivityEvent`
named `exception` carrying the error type, the message and the stack trace, taking those tag names
from `ErrorTags`. That makes `ErrorTags` the one convention class Abstractions consumes itself —
the other eight are vocabulary for the modules.

## The convention classes

Nine static classes of `const string`, one per instrumented area, in
`Pragmatic.Telemetry.Conventions`. They exist so that a tag name is written once and referenced,
rather than typed at each call site where a plural, a dot or an underscore can drift and silently
break correlation in the backend.

Two naming families live side by side, and the distinction is deliberate:

| Family | Example | Meaning |
|---|---|---|
| Standard OpenTelemetry names | `db.operation.name`, `db.collection.name`, `exception.message` | Names defined by the OTel semantic conventions. A backend that already understands them keeps working. |
| `pragmatic.*` prefixed names | `pragmatic.cache.key`, `pragmatic.db.filter_mode`, `pragmatic.job.type` | Concepts OTel has no name for, because they are framework concepts. The prefix keeps them from colliding with a future standard name. |

The constants in use today across the framework: `ActionTags` in Actions (`ActionInvokerBase`,
`MutationInvoker`), `EventTags` in Events (`InMemoryEventDispatcher`), `I18NTags` in
Internationalization (`I18NActivitySource`, `JsonLocalizationProvider`, `StringLocalizer`, the
culture middleware), `ResilienceTags` in Resilience (`ResiliencePipeline`), and `ErrorTags` through
`ActivityHelper`.

When you instrument your own spans alongside the framework, reference these constants rather than
retyping the strings — a tag that differs by one character does not correlate with the framework's
own spans, and nothing at compile time will tell you.

## `TelemetryOptions` and the startup gate

`TelemetryOptions` is a plain options object surfaced by the host as `PragmaticOptions.Telemetry`
and consumed by `PragmaticTelemetry.AddPragmaticTelemetry` in `Pragmatic.Composition.Host`.

**`SamplingRatio` validates in its setter**, throwing `ArgumentOutOfRangeException` outside the
inclusive range `[0.0, 1.0]`. That is not defensive decoration — it is where the value arrives from
configuration. The generated entry point binds the `Telemetry` section of your configuration
straight onto the instance:

```csharp
configuration.GetSection("Telemetry").Bind(options);
PragmaticTelemetry.AddPragmaticTelemetry(services, options, isDevelopment);
```

So `"SamplingRatio": 1.5` in `appsettings.json` fails at startup, with the offending value in the
message, instead of quietly clamping and leaving you to wonder why trace volume does not match the
number you wrote. The default is `0.1` — one trace in ten.

**In Development, sampling is not applied at all.** `PragmaticTelemetry` installs the
ratio-based sampler only when the environment is not Development *and* the ratio is below 1.0.
Locally you always see every trace, whatever the configured ratio says, because a sampled-away span
during debugging is a wasted afternoon.

## What the generator does

Nothing per type — there is no telemetry attribute, no model, no template. Telemetry is not a
detected feature either: `FeatureDetector` has no flag for it, because the wiring is unconditional.
The composition entry template (`PragmaticEntryTemplate.RenderTelemetrySetup`) emits the two lines
shown above into every generated entry point. Telemetry is always wired; what it *does* is decided
by `TelemetryOptions`, at runtime, from configuration.

## External references

Named here, described where they live:

- **`Pragmatic.Composition.Host` → `PragmaticTelemetry`** — the only consumer of
  `TelemetryOptions`; turns it into OpenTelemetry tracing, metrics and exporter registrations.
- **`Pragmatic.Composition.Host` → `PragmaticOptions`** — exposes `Telemetry` as the bindable
  section of the host options.
- **`Pragmatic.SourceGenerator` → `PragmaticEntryTemplate`** — emits the configuration bind and the
  `AddPragmaticTelemetry` call into the generated entry point.
- **`System.Diagnostics.Activity`** (BCL) — the type everything here extends. Pragmatic adds no
  tracing abstraction of its own on top of it.
