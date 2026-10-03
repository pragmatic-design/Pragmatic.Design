---
title: "Getting Started with Pragmatic.Composition"
description: "This guide walks through setting up a Pragmatic application from scratch, adding modules, and understanding how the pieces connect."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Composition/docs/getting-started.md
sidebar:
  order: 2
---
This guide walks through setting up a Pragmatic application from scratch, adding modules, and understanding how the pieces connect.

## Prerequisites

- .NET 10 SDK
- `Pragmatic.Abstractions` (attributes and interfaces)
- `Pragmatic.Composition.Host` (ASP.NET Core hosting runtime)
- `Pragmatic.SourceGenerator` (analyzer reference)

## Minimal Host

The simplest Pragmatic application needs three things:

1. A **module class** declaring the deployment topology
2. A **Program.cs** calling `PragmaticApp.RunAsync`
3. The source generator wired as an analyzer

### Step 1: Create the Host Project

```bash
dotnet new web -n MyApp.Host
cd MyApp.Host
dotnet add reference ../Pragmatic.Composition/src/Pragmatic.Composition/Pragmatic.Composition.csproj
```

Add the source generator in your `.csproj`:

```xml
<ProjectReference Include="..\Pragmatic.SourceGenerator\src\Pragmatic.SourceGenerator\Pragmatic.SourceGenerator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

### Step 2: Define a Module

Create an empty sealed class decorated with `[Module]`:

```csharp
using Pragmatic.Composition.Attributes;

namespace MyApp.Host;

[Module]
public sealed class MyAppModule;
```

This is the simplest possible module -- no database, no dependencies. The SG detects it as the host module because the project is an executable.

### Step 3: Program.cs

```csharp
using Pragmatic.Composition.Hosting;

await PragmaticApp.RunAsync(args).ConfigureAwait(false);
```

That is the entire `Program.cs`. The source generator creates the real `PragmaticApp.RunAsync` implementation based on discovered modules. The stub in `Pragmatic.Composition.Host` exists only for design-time IntelliSense before the generator runs.

## Adding Domain Modules

Domain logic lives in separate class library projects. Each module declares itself with `[Module]` and its dependencies with `[IncludeModule<T>]`.

### Step 1: Create a Domain Module

```bash
dotnet new classlib -n MyApp.Catalog
```

```csharp
// MyApp.Catalog/CatalogModule.cs
using Pragmatic.Composition.Attributes;

namespace MyApp.Catalog;

[Module(Name = "MyApp.Catalog", Description = "Product catalog management")]
public sealed class CatalogModule;
```

### Step 2: Include It in the Host

Reference the module project from the host and add it to the topology:

```csharp
// MyApp.Host/MyAppModule.cs
using Pragmatic.Composition.Attributes;
using MyApp.Catalog;

namespace MyApp.Host;

[Module]
[Include<CatalogModule>]
public sealed class MyAppModule;
```

### Step 3: Add a Database (Optional)

If the module needs persistence, declare a database and wire it:

```csharp
// MyApp.Host/MyAppDatabase.cs
using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Database;
using Pragmatic.Composition.Enums;

namespace MyApp.Host;

[PragmaticDatabase(Provider = DatabaseProvider.SqlServer, ConfigKey = "ConnectionStrings:App")]
public sealed class MyAppDatabase : PragmaticDatabase { }
```

Then update the module topology:

```csharp
[Module]
[Include<CatalogModule, MyAppDatabase>]
public sealed class MyAppModule;
```

The SG generates a DbContext for the Catalog boundary, wired to the App database connection string.

## Cross-Module Dependencies

When a module depends on types from another module (e.g., Booking needs Catalog for property lookups), declare it with `[IncludeModule<T>]`:

```csharp
// MyApp.Booking/BookingModule.cs
[Module(Name = "MyApp.Booking", Description = "Reservation management")]
[IncludeModule<CatalogModule>]
public sealed class BookingModule;
```

The host then includes both: it registers only what it declares, so hosting Booking without Catalog (or
without `[RemoteBoundary<CatalogModule>]`, if Catalog runs elsewhere) is **PRAG1603**:

```csharp
[Module]
[Include<CatalogModule, MyAppDatabase>]
[Include<BookingModule, MyAppDatabase>]
public sealed class MyAppModule;
```

## Configuring Module Strategies

Infrastructure choices are configured in `Program.cs` via the `IPragmaticBuilder` callback:

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    // Development convenience
    if (app.Environment.IsDevelopment())
        app.UseDatabaseEnsureCreated();

    // Multi-tenancy
    app.UseMultiTenancy(mt => mt.UseHeader());

    // Authentication
    app.UseAuthentication<NoOpAuthenticationHandler>("PragmaticDefault");

    // Logging
    app.UseLogging(log =>
    {
        log.AddConsole(PragmaticConsoleConfiguration.ForDevelopment());
    });
}).ConfigureAwait(false);
```

Each `Use*()` method comes from the corresponding Pragmatic module's extension methods on `IPragmaticBuilder`. You only see methods for packages you have referenced.

## Adding Business Wiring

Application-specific services and middleware go in an `IStartupStep` implementation:

```csharp
using Pragmatic.Composition.Abstractions;
using Pragmatic.Composition.Attributes;

namespace MyApp.Host;

[StartupStep]
[RequiresConfig("ConnectionStrings:App")]
public class MyAppStartupStep : IStartupStep
{
    public int Order => 60;

    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Register app-specific services
        services.AddOpenApi();
    }

    public void ConfigurePipeline(IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }
}
```

The `[RequiresConfig]` attribute makes the host validate that `ConnectionStrings:App` exists at startup. If missing, the application fails fast with a clear error message.

## Registering Services

Mark services with `[Service]` in your domain modules. The SG generates all DI registration code:

```csharp
// MyApp.Catalog/Services/ProductService.cs
using Pragmatic.Composition.Attributes;

namespace MyApp.Catalog.Services;

[Service]
public class ProductService(IProductRepository repository) : IProductService
{
    // Implementation
}
```

No manual `services.AddScoped<IProductService, ProductService>()` needed. The SG generates it and emits a `[PragmaticMetadata]` assembly attribute. The host generator reads it and includes the registration in the aggregated startup code.

## Complete File Listing

A typical Pragmatic application looks like this:

```
MyApp.Host/
    Program.cs                  # PragmaticApp.RunAsync(args, app => { ... })
    MyAppModule.cs              # [Module] + [Include<...>] topology
    MyAppDatabase.cs            # PragmaticDatabase declaration
    MyAppStartupStep.cs         # [StartupStep] + IStartupStep

MyApp.Catalog/
    CatalogModule.cs            # [Module] declaration
    Services/
        ProductService.cs       # [Service] auto-registered
    Entities/
        Product.cs              # Domain entity

MyApp.Booking/
    BookingModule.cs            # [Module] + [IncludeModule<CatalogModule>]
    Services/
        ReservationService.cs   # [Service]
```

## Next Steps

- [Startup Pipeline](/modules/composition/startup-pipeline/) -- understand `IStartupStep` ordering and the two-phase lifecycle
- [Service Registration](/modules/composition/service-registration/) -- `[Service]`, `[Decorator]`, `[ServiceFactory]`, keyed services
- [Remote Boundaries](/modules/composition/remote-boundaries/) -- split modules across separate hosts
