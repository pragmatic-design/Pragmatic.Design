---
title: Architecture
description: The 3-tier configuration model, module layers, and composition rules.
---

Pragmatic Design is a **composition of modules**. Each module is independently useful and works alone. Put them together and they compose: the unified Source Generator sees the combination and wires the cross-module glue.

## Tier pyramid

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
| **Building Block** (Layer 0-2) | Toolkit infrastructure. You use it to build your domain. | Result, Actions, Persistence, Composition, Events, Messaging, Jobs |
| **Medium Block** | Cross-cutting packages that ship complete. Include, configure, use. | Identity.Local, Comments, Tags, Attachments, Notes |
| **Product / App** | Composition of blocks into a running application. | Showcase, your own services |

## Building block layers

Inside the Building Block tier, modules are stratified: a module depends only on its own layer or below.

```
Layer 0 (Foundation)     Layer 1 (Capabilities)     Layer 2 (Integration)
├── Abstractions         ├── Validation             ├── Actions
├── Result               ├── Mapping                ├── Endpoints
├── Ensure               ├── Specification          ├── Persistence + EFCore
                         ├── Internationalization   ├── Events + EFCore
                         ├── Caching                ├── Composition + Host
                         ├── Resilience             ├── Authorization
                         ├── Configuration          ├── Identity
                         └── Temporal               ├── MultiTenancy
                                                    ├── Messaging
                                                    ├── Jobs
                                                    ├── Migrations
                                                    └── Logging
```

Beside the layers:

- **Supporting**: `Storage`, `FeatureFlags`, `Discovery`, `Patch`, `Client`.
- **Compliance**: `Privacy`, `Audit`, `Redaction`, `Cryptography`, `Incidents`.
- **Documents & media**: `Documents`, `Imaging`, `Email`, `Notifications`.
- **Platform (preview)**: `Agent`, `Gateway`.

## Composition by presence

This is the core architectural insight: **adding a NuGet reference is the configuration**.

When you add `Pragmatic.Persistence.EFCore` to your project, the Source Generator detects it (by looking for a marker type; see [Feature detection](/source-generator/feature-detection/)) and starts generating repositories, entity configurations and query filters for every `[Entity]` class in your code. Remove the package, and the generated code disappears.

No feature flags. No configuration files. No `if` statements in startup. Your `.csproj` is the source of truth.

When modules are present together, **cross-module composition** follows:

- `[DomainAction]` + `Pragmatic.Persistence.EFCore` → the action can declare `[LoadEntity<Reservation>]`, and the generated invoker loads the row before `Execute` runs.
- `[DomainAction]` + `Pragmatic.Authorization` → the invoker checks the declared permission before executing.
- `IDomainEventHandler<T>` + an outbox on the boundary → the events of a save are written in the same transaction and delivered after the commit.

None of these need explicit wiring. You added the package, you wrote the declaration, the generator did the rest.

## Three-tier configuration model

```
Topology (compile-time)   →   Module Strategy (Program.cs)   →   Business Wiring (IStartupStep)
     SG auto-detect                IPragmaticBuilder                 IStartupStep
```

### Tier 1: Topology (Source Generator)

You **declare** the structure with attributes; the generator reads it.

```csharp
[Module]                                          // application module
public sealed class BookingModule;

[Boundary]                                        // logical boundary inside a module
public sealed class ReservationsBoundary;

[BelongsTo<ReservationsBoundary>]                 // which boundary an operation or entity is in
[DomainAction]
[Endpoint(HttpVerb.Post, "/reservations")]
public partial class CreateReservation : DomainAction<ReservationResult>
{
    // …
}
```

No list to keep in sync. Move the file or rename the class, and the topology follows.

### Tier 2: Module Strategy (`IPragmaticBuilder`)

For modules with a **pluggable backend**, you pick it in `Program.cs`:

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    app.UseJwtAuthentication(jwt => jwt.SigningKey = app.Configuration["Jwt:Key"]!);
    app.UseAuthorization(authz => authz
        .MapRole<BookingManager>()
        .UsePermissionCache(TimeSpan.FromMinutes(5)));
    app.UseMessaging(msg => msg.UseChannels());
    app.UseJobs(jobs =>
    {
        jobs.UseEfCore();
        jobs.UseEfCorePersistence();
    });
});
```

A `Use*()` is a choice. Where a module has a sensible default (an in-memory store, an in-process transport, a passthrough cache), it applies without one; where none is safe, the host refuses to start and says what is missing.

See the [Configuration reference](/reference/configuration/) for the full list.

### Tier 3: Business Wiring (`IStartupStep`)

Anything specific to your service (domain services, custom middleware) goes in one or more `IStartupStep` implementations. The Source Generator discovers them, no registration needed:

```csharp
public sealed class BookingStartup : IStartupStep
{
    public int Order => 100;  // lower runs first (default 0)

    public void ConfigureServices(
        IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddScoped<IRoomPricingService, DynamicPricingService>();
        services.AddDataScopeRule<HighSeasonRoomScopeRule, Room>();
    }

    public void ConfigurePipeline(IApplicationBuilder app)
    {
        app.UseMiddleware<RequestTimingMiddleware>();
    }
}
```

## Decision tree

> "Where does this go?"

| Question | Answer |
|----------|--------|
| "Where does this action / entity belong?" | Topology: the namespace, or `[BelongsTo<TBoundary>]` |
| "Which cache provider?" / "Which auth handler?" | Module Strategy: `app.UseXxx()` |
| "I need a domain service registered" | Business Wiring: `IStartupStep.ConfigureServices` |
| "I need custom middleware" | Business Wiring: `IStartupStep.ConfigurePipeline` |
| "I need a handler for a domain event" | Nothing: declare `IDomainEventHandler<T>`, the generator discovers it |
| "Change how a module itself behaves" | Tier 2 if supported; otherwise open an issue |

## Reading further

- [Configuration reference](/reference/configuration/): every `Use*()` method listed
- [How the Source Generator works](/source-generator/how-it-works/): pipeline, transforms, templates
- [Feature detection](/source-generator/feature-detection/): the marker-type mechanism
- Pick a module from the sidebar and open its **Overview**
