---
title: "Consuming Generated Docs"
description: "The source generator emits **living documentation** as compile-time `const` strings — a glossary, a C4"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/docs/howto/consuming-generated-docs.md
sidebar:
  order: 8
---
The source generator emits **living documentation** as compile-time `const` strings — a glossary, a C4
container diagram, and an AsyncAPI event contract. They are always in sync with the code (no annotations,
no drift) and cost nothing at runtime. This guide shows how to surface them.

## What gets generated

Each is generated **per assembly** that contains the relevant declarations, in the
`{AssemblyName}.Generated` namespace:

| Constant | Generated in | From | Content |
|----------|--------------|------|---------|
| `PragmaticGlossary.Markdown` | every assembly with `[Entity]` types | entities + their XML-doc summaries | Markdown glossary grouped by namespace |
| `PragmaticArchitecture.C4ContainerDiagram` | the host (has `[Include<…>]` / `[RemoteBoundary<…>]`) | the module topology | Mermaid container diagram (remote boundaries marked) |
| `PragmaticAsyncApi.Json` | every assembly with `IDomainEvent` types | domain events + payload schema | AsyncAPI 3.0 document |
| `PragmaticSagaDiagrams.{Saga}` | every assembly with `[Saga<TState>]` classes (namespace `Pragmatic.Messaging.Generated`) | the saga state machine | Mermaid `stateDiagram-v2` per saga (timeouts, compensations, terminal states) |
| `PragmaticUseCases.All` / `.Markdown` | every assembly with `[UseCase]` or `[Rule]` | the annotated operations | typed `UseCaseDescriptor` list — id, title, rules, target, file, line — and the same as Markdown |

### The use-case catalog

The one entry above that is **not** inferred: it carries what the author wrote, and the framework
cannot invent it. Annotate an operation with the identifier the requirement has and the rules it
enforces, in the words the person who asked for them would use:

```csharp
[Mutation(Mode = MutationMode.Update)]
[UseCase("BKG-CANCEL", Title = "Cancel a reservation")]
[Rule("A reservation can be cancelled only before its tenant's cancellation window closes")]
[Rule("Cancelling a reservation tells the rest of the system, by raising ReservationCancelled")]
public partial class CancelReservationMutation : Mutation<Reservation, ConflictError> { }
```

The catalog is typed, so a test can assert against it — which is the point: a traceability document
that a build breaks when it stops being true.

```csharp
var cancel = Booking.Generated.PragmaticUseCases.All.Single(u => u.Id == "BKG-CANCEL");
cancel.Rules.Should().HaveCount(2);
cancel.File.Should().EndWith("CancelReservationMutation.cs");   // project-relative, never absolute
```

A `[Rule]` written with no `[UseCase]` beside it lands in `PragmaticUseCases.RulesWithoutAUseCase`
rather than being dropped.

> Because they are per-assembly, you reference the constant on the assembly you care about — e.g. the
> glossary of the `Catalog` boundary is `Catalog.Generated.PragmaticGlossary.Markdown`, the C4 diagram of
> the host is `MyApp.Host.Generated.PragmaticArchitecture.C4ContainerDiagram`.

## Serve them from endpoints

They are plain strings, so a minimal-API endpoint is all you need:

```csharp
// Architecture (Mermaid) — render it in a docs page or paste into mermaid.live
app.MapGet("/docs/architecture.mmd", () =>
    Results.Text(MyApp.Host.Generated.PragmaticArchitecture.C4ContainerDiagram, "text/plain"));

// Event contract (AsyncAPI 3.0) — point an AsyncAPI Studio / Microcks at it
app.MapGet("/asyncapi.json", () =>
    Results.Text(Catalog.Generated.PragmaticAsyncApi.Json, "application/json"));

// Ubiquitous-language glossary (Markdown)
app.MapGet("/docs/glossary.md", () =>
    Results.Text(Catalog.Generated.PragmaticGlossary.Markdown, "text/markdown"));
```

Gate them behind an admin policy if you don't want them public:

```csharp
app.MapGet("/asyncapi.json", () => Results.Text(Catalog.Generated.PragmaticAsyncApi.Json, "application/json"))
   .RequireAuthorization("Admin");
```

### Aggregating across boundaries

Each boundary emits its own constant. To present one document, concatenate or merge at startup:

```csharp
// One combined glossary across boundaries
var glossary = string.Join("\n\n",
    Catalog.Generated.PragmaticGlossary.Markdown,
    Booking.Generated.PragmaticGlossary.Markdown);
app.MapGet("/docs/glossary.md", () => Results.Text(glossary, "text/markdown"));
```

The C4 diagram is already whole-system (generated on the host from the composed modules), so no
aggregation is needed for it.

## Use the AsyncAPI as a contract test

The AsyncAPI document carries each event's **payload schema**, so snapshotting it turns "did an event's
shape change?" into a build-time check. With [Verify](https://github.com/VerifyTests/Verify):

```csharp
[Fact]
public Task EventContract_IsStable()
    => Verify(Catalog.Generated.PragmaticAsyncApi.Json);
```

A reviewed change to a public event shows up as a snapshot diff; an *unintended* breaking change fails the
test. Events are tagged in the document:

- `x-pragmatic-public: true` — a public integration event (`IIntegrationEvent` / `[PublicEvent]`), the
  cross-boundary contract. Evolve these additively.
- `x-pragmatic-obsolete: true` — deprecated (`[ObsoleteEvent]`); remove once consumers have migrated.

## Where to subscribe: the channel's address

Each channel's **key** is the event's fully qualified type name — the document's identifier, stable
against an event being added elsewhere — and its **`address`** is the topic the transport carries it on,
which is what a consumer configures. Every event of one boundary shares that address: for
`Casework.Intake.Events.VerificationRequested` and `…CaseDecided` it is `intake.events` for both.

⚠️ The document is written at compile time and `IMessageRouter` is a runtime service, so the addresses
come from mirroring the default convention — `{boundary}.events`, from the kebab-cased second segment of
the namespace — and the document says so in **`x-pragmatic-address-rule`**. An application that
registers its own `IMessageRouter` routes by its own rule, and that field is how a consumer knows the
addresses do not describe it.

See [Events — Integration events](/modules/events/concepts/) for the two-level event model.
