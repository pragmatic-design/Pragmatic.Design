---
title: Introduction
description: What Pragmatic Design is, why it exists, and who it's for.
---

**Describe your domain. One source generator writes the application around it.**

**Pragmatic Design** is a .NET 10 framework for line-of-business software. You write what is specific to your business (entities, the operations on them, the rules they obey) and a single incremental source generator writes what is not: persistence, HTTP endpoints, validation, authorization, events, caching, DI wiring, OpenAPI. It generates **plain C# you can read**, it **refuses at compile time** what would fail at runtime, and the code it writes into your project **is yours**. The generated wiring uses no runtime reflection; EF Core, where you use it, keeps its own runtime behaviour.

:::note[v1.0.0-alpha]
A core set is stable: Abstractions, Ensure, Result, Specification, Mapping, Validation, Temporal, Storage and the core of Persistence. The other modules, the Platform layer (Agent, Gateway) among them, are functional or in preview, and APIs may still change before the 1.0 release. **New here? Start with [Installation](/getting-started/installation/) to write the code yourself, or [Build with an agent](/getting-started/with-an-agent/) to have a coding agent write it.**
:::

## The problem

Every .NET service ends up re-implementing the same scaffolding:

- repository classes that wrap `DbContext<T>`
- endpoint registration and request binding
- validation pipelines, authorization filters
- DI `AddScoped` lists that drift out of sync
- entity configuration, audit fields, soft-delete
- outbox, saga, retry, cache-key generation
- cross-module glue that silently rots

This code is repetitive, error-prone, easy to miss in review, and **it isn't yours**: it's infrastructure. Hand-writing it again for every service is the worst kind of friction: it slows the first week and keeps slowing you forever.

## The approach

You write the **declaration**. This is an entity from the Showcase, the hotel-booking reference application (abridged):

```csharp
[Entity]
[Auditable]
[SoftDelete]
[StateMachine<ReservationStatus>]
[Relation.ManyToOne<Guest>]
public partial class Reservation : DomainEventSource, IEntity
{
    [FutureDate]
    public DateTimeOffset CheckIn { get; private set; }

    [GreaterThanProperty(nameof(CheckIn))]
    public DateTimeOffset CheckOut { get; private set; }

    public ReservationStatus Status { get; private set; } = ReservationStatus.Pending;
}
```

And an operation on it, an HTTP endpoint that checks a permission, loads the reservation, runs the transition, commits, and answers `409` when the state machine says no, with no body:

```csharp
[Endpoint(HttpVerb.Post, "/{id}/confirm")]
[EndpointGroup<ReservationsGroup>]
[RequirePermission(BookingPermissions.Reservation.Update)]
[Mutation(Mode = MutationMode.Update)]
[TransitionsTo<ReservationStatus>(ReservationStatus.Confirmed)]
public partial class ConfirmReservationMutation : Mutation<Reservation, ConflictError>
{
    public required Guid Id { get; init; }
}
```

The generator writes everything else:

- the `Create()` factory, typed setters, the repository, relations and includes
- the audit stamps, the soft-delete filter and the state machine's guarded transitions
- the validator of `[FutureDate]` and `[GreaterThanProperty]`
- the EF Core configuration of `Reservation`, in the host
- the mutation invoker: load, permission, validation, transition, commit, events
- the endpoint at `POST /{id}/confirm`, with typed binding and the `409` documented in OpenAPI

Every generated file is ordinary C#. Add `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` to the project and read them under `obj/`. No bytecode manipulation, no runtime discovery.

## Principles

- **Source Generator First**: no reflection in the generated wiring (EF Core excepted), AOT-ready, errors at compile time, not at first request.
- **Composition by presence**: add a NuGet, the generator detects it and wires up. Remove it, the generated code vanishes. No feature flags, no `if` statements in config.
- **Host decides**: your app's `.csproj` is the source of truth for what's active. Module strategy (`IPragmaticBuilder.Use*()`) in `Program.cs` picks implementations.
- **Defaults where they are safe**: in-memory stores, in-process transports, a passthrough cache. Where no default is safe, the host refuses to start and says what is missing.
- **Result over exceptions**: domain errors are values, not throws. See `Pragmatic.Result`.
- **No hidden reflection**: if you think you need `typeof(T).GetProperties()`, ask the generator to produce the accessor instead.

## Architecture at a glance

```
                    ┌─────────────────────┐
                    │   Product / App     │  Showcase, your services
                    ├─────────────────────┤
                    │    Medium Block     │  Identity.Local, Comments, Tags, Attachments, Notes
                    ├─────────────────────┤
                    │   Building Block    │  Result, Actions, Persistence, Messaging, …
                    └─────────────────────┘
```

| Tier | Scope | Examples |
|------|-------|----------|
| **Building Block** | Toolkit infrastructure. You use it to build your domain. | Result, Actions, Persistence, Composition, Events, Messaging, Jobs, Migrations |
| **Medium Block** | Cross-cutting packages that ship complete. Include, configure, use. | Identity.Local, Comments, Tags, Attachments |
| **Product / App** | Composition of blocks into a running application. | Showcase (E2E reference), your own services |

See [`Architecture`](/getting-started/architecture/) for the layering and how modules compose.

## Who it's for

You probably want Pragmatic Design if:

- you ship .NET services in production and spend energy fighting boilerplate
- you value explicit, inspectable code over framework magic
- you want AOT compilation as a real option, not an aspiration
- you've used MediatR / MassTransit / Hangfire / AutoMapper / FluentValidation and like some, but want them **composed** instead of stitched together

You probably don't want it (yet) if:

- you need a framework that hides all the plumbing behind a single decorator
- you depend on an ecosystem that is not first-class here yet
- you can't run .NET 10

## Status

The first public release is **`1.0.0-alpha`**: 45 modules, shipped as NuGet packages. The Showcase and four application examples (Time off, Invoicing, Casework, Warehouse) show how they compose, each with its own test suite against real databases and brokers, and every module has runnable samples. APIs may still change before 1.0; each module's status is in the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

## Next steps

- [Installation](/getting-started/installation/): get it building in minutes
- [Build with an agent](/getting-started/with-an-agent/): the skills for Claude Code, Codex and other agents
- [Architecture](/getting-started/architecture/): 3-tier model, module layers, Medium Blocks
- [How the SG works](/source-generator/how-it-works/): the one unified generator
- [Showcase walkthrough](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/howto/showcase-walkthrough.md): end-to-end reference app
