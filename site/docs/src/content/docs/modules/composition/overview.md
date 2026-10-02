---
title: "Pragmatic.Composition"
description: "Source-generated application composition and dependency injection for .NET 10. Declare modules,"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Composition/README.md
sidebar:
  order: 0
  label: Overview
---
Source-generated application composition and dependency injection for .NET 10. Declare modules,
services, and startup steps; the generator writes all the wiring, so the composed application starts
without reflection and without scanning assemblies.

Convention-based scanning lives in a separate package, `Pragmatic.Composition.Scanning`, for the cases
the generator cannot see. It loads assemblies and enumerates types at run time and is neither trim- nor
AOT-safe; referencing it is the opt-in, and nothing in the generated path calls it.

## The Problem

Every .NET app accumulates the same startup ceremony: register services one by one, order middleware,
wire DbContexts, keep it consistent across modules. With 50+ services, `Program.cs` becomes a wall of
`services.AddScoped<>()` that no one wants to maintain, and forgetting one line silently breaks the app.

```csharp
// Without Pragmatic: 80+ lines of mechanical wiring
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IProductService, ProductService>();
// ... 50 more, growing with every new class
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(cs));
app.UseAuthentication(); app.UseAuthorization(); // forget one and it breaks
```

## The Solution

Declare **what** your application is; the generator handles **how** it starts up.

```csharp
// Program.cs: one call wires everything
await PragmaticApp.RunAsync(args, app =>
{
    app.UsePragmaticMigrations();
    if (app.Environment.IsDevelopment()) app.UseDevelopmentIdentity();   // X-User-* headers are the caller
});
```

```csharp
// Module topology: one class per bounded context
[Module]
[Include<OrdersModule, AppDatabase>]
[Include<BillingModule, FinancialDatabase>]
public sealed class MyAppModule;

// Services register themselves
[Service]
public class OrderService(IOrderRepository repository) : IOrderService { }
```

The generator emits the complete host startup (infrastructure auto-registration, ordered startup
steps, database init, endpoint mapping, telemetry, maintenance mode) at compile time. Cross-assembly
discovery works through `[PragmaticMetadata]` assembly attributes: libraries declare what they
register, the host aggregates automatically.

## Packages

| Package | Role |
|---------|------|
| `Pragmatic.Composition` | Meta-package (references Abstractions + Host) |
| `Pragmatic.Composition.Host` | ASP.NET Core runtime: `PragmaticApp`, `IStartupStep`, telemetry, remote boundaries, maintenance mode |
| `Pragmatic.Composition.Scanning` | Opt-in convention scanning at run time, not trim- or AOT-safe (see above) |

Attributes (`[Service]`, `[Module]`, `[StartupStep]`, …) live in `Pragmatic.Abstractions`, so domain
modules use them without referencing ASP.NET Core.

## The 3-tier configuration model

| Tier | Where | What it decides |
|------|-------|-----------------|
| **Topology** (compile-time) | `[Module]`, `[Boundary]`, `[BelongsTo<T>]`, `[UsePackage<T>]` | Structure & module dependencies; the generator detects it |
| **Module strategy** | `Program.cs` via `IPragmaticBuilder.Use*()` | Infrastructure choices: auth handler, storage, transport |
| **Business wiring** | `IStartupStep` | Services, filters, OpenAPI, feature-specific DI + HTTP pipeline |

What a module declares is wired without a line in `Program.cs`, with in-memory or passthrough defaults
where a backend is optional; you override only what you need. Two things are never defaulted, because
guessing them would be unsafe: **who calls the API** (every endpoint requires an authenticated caller,
the build stops on PRAG1695 without an identity package or `[AnonymousHost]`, and outside Development a
host with no authentication method refuses to start) and **a culture**, from `UseI18N` or the modules'
translations. See [Startup Pipeline](/modules/composition/startup-pipeline/).

## Installation

```bash
dotnet add package Pragmatic.Composition.Host
dotnet add package Pragmatic.SourceGenerator   # the unified analyzer
```

(Building inside this monorepo? See [Monorepo Structure](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/howto/monorepo-structure.md).)

## What the generator gives you

- **`[Service]` / `[Decorator]`**: self-registering services and ordered decorators, no `AddScoped` lists.
- **`[Module]` / `[Include<…>]`**: module topology and per-module databases.
- **`[StartupStep]` / `IStartupStep`**: ordered business wiring + HTTP pipeline configuration.
- **`IPragmaticBuilder.Use*()`**: module strategy (auth, storage, messaging, …).
- **Remote boundaries**: `[RemoteBoundary<T>]` generates typed HTTP invokers for cross-service calls.
- **Maintenance mode**, telemetry, and database initialization, wired automatically.

## Status

**Functional** within 1.0.0-alpha: the composition model, service/decorator registration, startup steps,
the builder, remote boundaries, and maintenance mode. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

| [Concepts](/modules/composition/concepts/) | Composition model, metadata aggregation, the 3-tier configuration model |
| [Getting Started](/modules/composition/getting-started/) | Your first module, service, and `PragmaticApp.RunAsync` |
| [Service Registration](/modules/composition/service-registration/) | `[Service]`, lifetimes, `[Decorator]`, keyed services |
| [Startup Pipeline](/modules/composition/startup-pipeline/) | `IStartupStep`, `IPragmaticBuilder`, ordering, HTTP pipeline |
| [Remote Boundaries](/modules/composition/remote-boundaries/) | `[RemoteBoundary<T>]`, typed HTTP invokers, the invoke endpoint |
| [Common Mistakes](/modules/composition/common-mistakes/) | The most frequent composition pitfalls |
| [Troubleshooting](/modules/composition/troubleshooting/) | Problem/solution guide with diagnostics |

## Requirements

- .NET 10.0+
- ASP.NET Core 10.0+
- `Pragmatic.SourceGenerator` analyzer

## License

Part of the [Pragmatic.Design](/modules/composition/overview/) ecosystem. See [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Composition is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
