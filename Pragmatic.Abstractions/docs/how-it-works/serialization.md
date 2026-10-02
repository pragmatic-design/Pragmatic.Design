# Serialization — one JSON pipeline, chosen at compile time

> **Scope**: `src/Pragmatic.Abstractions/Serialization/` — 6 files.
> `PragmaticJsonOptions` · `PragmaticJsonServiceCollectionExtensions` · `PragmaticJsonBuilderExtensions` ·
> `PragmaticCommonJsonContext` · `PragmaticGenerateJsonContextAttribute` · `IRedactionMap`.
> Plus `src/Pragmatic.Abstractions/NotLoggedAttribute.cs`, which lives at the project root but is the
> attribute `IRedactionMap` is the compiled form of.
>
> **Not covered here**: `Pragmatic.Abstractions.Analyzers` is a separate project inside the same
> module — PRAG2800 is named below because it is where the seam's strict mode applies, but the analyzer's
> own rules are catalogued in [interfaces §26](../interfaces.md#26-analyzers-pragmaticabstractionsanalyzers).
> Consumers outside Abstractions are named where they matter, not opened.

For the member-by-member catalogue, see [interfaces](../interfaces.md#21-serialization-aot-first-json-seam).
This document is about how the pieces fit together, and why they are shaped that way.

## The problem it solves

Six subsystems serialize JSON on a Pragmatic host: the messaging serializer, the event outbox, the
message outbox, saga state, job parameters, and the host itself. Left alone, each would build its
own `JsonSerializerOptions`, and a native AOT publish would have to be fixed six times — or, worse,
would silently keep working under reflection until the first trimmed deploy.

`PragmaticJsonOptions` is the single object all six share. It is not an options *class* in the
`IOptions<T>` sense: it is registered **as an instance**, not by type, precisely so that the host's
configuration and every module's resolution observe the same object graph rather than two copies
reconciled by the container. One call site sits awkwardly against that: `JobContinuation`'s factories
are `static`, so they have no container to resolve from. `Then<TJob, TParams>(parameters, jsonOptions)`
takes the configured instance as an argument and is the overload to use; the one-argument overload has
nothing to fall back on but `PragmaticJsonOptions.Default`, where contexts added via `UseJson(...)` —
and a disabled reflection fallback — do not apply.

## How the chain is built

`PragmaticJsonOptions` holds an ordered list of `IJsonTypeInfoResolver` and a boolean.
`Build()` turns them into one read-only `JsonSerializerOptions`: camelCase naming, the registered
contexts in registration order, and — unless disabled — a `DefaultJsonTypeInfoResolver` appended
last as a catch-all. Multiple resolvers are folded with `JsonTypeInfoResolver.Combine`, so the first
context that knows a type wins.

Three properties of that design carry weight:

- **The reflection fallback is on by default.** Adopting the seam therefore changes nothing for an
  existing application; AOT-safety is something you opt into, not something that breaks you on
  upgrade. `DisableReflectionFallback()` is the opt-in, and after it every serialized type must be
  covered by a registered context or serialization throws.
- **`Build()` freezes the object.** Once built, `AddContext` and `DisableReflectionFallback` throw
  `InvalidOperationException`. The build is lazy rather than tied to startup, and its consumers
  decide when it happens: `JsonMessageSerializer` calls it in a field initializer and `JobScheduler`
  in its constructor, so resolving either one freezes the options before a single payload is
  written. That is why the internal list is guarded by a `Lock` — startup configuration and the
  first resolution can otherwise race.
- **`AddContextOnce` deduplicates by instance**, not by type. Framework packages whose registration
  may run more than once pass their context's `Default` singleton and stack nothing; two distinct
  `new AppJsonContext()` objects would both be added. Pass `Default`.

`PragmaticCommonJsonContext` is seeded into the list in the field initializer, so **every** instance
starts with it. It covers two types — `Dictionary<string, string>` and `string[]` — which sound
trivial until you need them at a fixed-type call site with no options object in reach. That is what
it is for: EF value converters and stores serialize through its `JsonTypeInfo` directly, with no
options and no reflection. `Pragmatic.Internationalization.EFCore`'s
`LocalizedStringValueConverter`, `Pragmatic.Notifications.EFCore`'s `EfCoreNotificationStore`, and
`Pragmatic.Identity.Oidc`'s `OidcRoleClaimsTransformer` all use it that way.

## The two ways contexts arrive

| Path | Who calls it | What it does |
|---|---|---|
| `services.AddPragmaticJson()` | each module that serializes, from its own registration — `Pragmatic.Messaging.Core`, `Pragmatic.Jobs` | ensures the shared singleton exists, nothing more |
| `services.AddPragmaticJsonContext(ctx)` | generated code | contributes one context idempotently |
| `builder.UseJson(...)` / `UseJson<TContext>()` | the host, once | hands you the live instance to add contexts and turn the fallback off |

`GetOrAddOptions` is what makes the three converge: it scans the `IServiceCollection` for an
already-registered `PragmaticJsonOptions` instance and returns it, creating one only if absent. Call
order between host and modules therefore does not matter.

`DisableReflectionFallback()` is a public method on the shared instance, so `UseJson(...)` is the
intended convention and not a barrier: any module that gets hold of the singleton can impose strict
mode on the application consuming it. Keeping the call in the host is what makes the strict AOT mode
a decision taken in one place.

## What `[assembly: PragmaticGenerateJsonContext]` turns on

The generator's serialization feature activates on any of three signals: the build property
`PragmaticGenerateJsonContext`, `PublishAot=true`, or the assembly attribute. The attribute exists
in addition to the property for project-reference scenarios, where a package's `.props` are not
imported and the build property never reaches the compiler.

The types it collects are the assembly's *boundary* types, found by attribute:

- message handlers,
- `[Job]` and `[RecurringJob]` parameter types,
- event handlers,
- sagas,
- `[MapFrom]` and `[MapTo]` mapped DTOs,
- the item type of an SSE streaming endpoint, serialised per event through the host options seam,

each closed transitively through `JsonShapeExtractor`. Raw entity returns and projection/patch DTOs
are not part of that closure — a type that reaches JSON only through one of those routes needs its
own context registered through `UseJson(...)`, or the reflection fallback left on.

Three files come out, all under `Features/Serialization/Templates/`:

| Output | Contents |
|---|---|
| `_Infra.Json.Context.g.cs` | `PragmaticJsonContext`, built directly on `JsonMetadataServices` rather than delegating to System.Text.Json's own generator |
| `_Infra.Json.Registration.g.cs` | `AddGeneratedJsonContext(this IServiceCollection)`, which calls `AddPragmaticJsonContext(PragmaticJsonContext.Default)` |
| `_Metadata.JsonContexts.g.cs` | an assembly-level `[PragmaticMetadata]` entry, category `JsonContexts` (20), carrying the registration method's fully-qualified name |

The metadata entry is how the host finds it. `PragmaticHostTemplate` reads category 20 from every
referenced assembly and calls each registration method it finds, so a boundary library that opted in
contributes its context to the host with no wiring on either side.

**PRAG2800** checks part of the same ground. The analyzer compares payload types against the registered
contexts and reports the ones with no coverage — but for three of the markers above only, message
handlers, domain-event handlers and jobs, at `Info` severity, and only once the assembly declares a
`JsonSerializerContext` of its own. Each of those three limits is deliberate, and the reasoning is in
[analyzers](analyzers.md#prag2800--the-payloads-the-generator-could-not-cover). Outside those three,
`DisableReflectionFallback()` stays a promise checked at runtime, on whichever payload happens to be
serialized first.

## `[NotLogged]` and `IRedactionMap`

`[NotLogged]` marks a member whose value must not reach a log or an audit entry. It marks and does
not enforce: nothing in the framework acts on the mark at runtime. What it does produce is a compiled
form of it — because the runtime does no reflection, for every messaging assembly that contains at
least one the generator emits
`{Assembly}.Generated.GeneratedMessageRedactionMap`, a sealed `IRedactionMap` in
`_Infra.Messaging.RedactionMap.g.cs`.

Its shape is deliberate. Static string arrays plus a chain of `if (type == typeof(X))` in
`TryGetRedactedProperties` — a lookup with no reflection and no dictionary allocation. The names it
returns are the **serialized** names only where a member carries `[JsonPropertyName]`, in which case
the map holds the JSON name, because a renamed member would otherwise be unfindable in serialized
output. Everywhere else it holds the CLR name, in PascalCase, while the seam writes camelCase.
Entries are per type, not per handler, and include nested types reachable from the message.

Registration is `TryAddEnumerable(Singleton<IRedactionMap, GeneratedMessageRedactionMap>)`, so a host
composed of several messaging assemblies can resolve `IEnumerable<IRedactionMap>` and get one entry
per assembly. No framework component resolves it: message auditing writes through the framework audit
trail, whose entries carry no payload field, so there is no serialized payload left to redact at that
point. Assemblies with no `[NotLogged]` generate nothing at all.

The framework's own redaction is pattern-based rather than marker-based: `PragmaticDataRedactor` in
`Pragmatic.Logging` works from configured property-name patterns, and the audit trail redacts what it
stores through `PersonalDataRedactor`. For declared personal data with end-to-end enforcement, the
attribute to reach for is `[PersonalData]` from `Pragmatic.Privacy.Abstractions`. `IRedactionMap` is
the compile-time map for a serialization boundary you own — material to consume, not a mechanism that
runs: inject the enumerable, ask it for the type you are about to write, and skip the properties it
names, **compared case-insensitively**, or an un-renamed `Password` will not match the `password` in
the payload.

## External references

Named here, described where they live:

- **`Pragmatic.Messaging.Core` → `JsonMessageSerializer`** — serializes every message payload through
  the shared options; its `[RequiresUnreferencedCode]` message is what points a trimming user at
  `UseJson(...)`.
- **`Pragmatic.Events.EFCore` → `EventOutboxInterceptor`, `EventOutboxDeliveryService`** and
  **`Pragmatic.Messaging` → `OutboxInterceptor`, `EfCoreSagaRepository`** — the persisted-payload
  boundaries; what they write must be readable by a later process, so they share one context chain.
- **`Pragmatic.Jobs` → `JobScheduler`, `JobContinuation`** — job parameters are serialized on
  schedule and deserialized on execution, possibly on another host.
- **`Pragmatic.Abstractions.Analyzers` → `JsonContextCoverageAnalyzer`** — emits PRAG2800.
- **`Pragmatic.Composition`** — the generated host aggregates `JsonContexts` metadata and calls each
  assembly's `AddGeneratedJsonContext`.
