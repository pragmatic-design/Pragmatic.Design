---
title: "Getting Started with Pragmatic.Configuration"
description: "This guide covers the two pillars of Pragmatic.Configuration: **compile-time binding** via the source generator and **runtime services** for stores, caching, an"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Configuration/docs/getting-started.md
sidebar:
  order: 2
---
This guide covers the two pillars of Pragmatic.Configuration: **compile-time binding** via the source generator and **runtime services** for stores, caching, and resolution.

## Prerequisites

- .NET 10.0+
- `Pragmatic.Abstractions` (provides `IConfigurationStore`, `ISecretStore`, `ICacheStack`, `EnvironmentProfile`, `ConfigurationChange`)
- `Pragmatic.SourceGenerator` analyzer reference (for `[Configuration]` attribute)

## Step 1: Add the Package

```bash
dotnet add package Pragmatic.Configuration
```

For the source generator:

```xml
<ProjectReference Include="..\Pragmatic.SourceGenerator\...\Pragmatic.SourceGenerator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

## Step 2: Define a Configuration Class

Annotate a `partial` class with `[Configuration]`. The source generator produces `IOptions<T>` binding, `DataAnnotation` validation, and DI registration.

```csharp
using Pragmatic.Configuration;
using System.ComponentModel.DataAnnotations;

[Configuration]
public partial class BookingOptions
{
    [Required]
    [MaxLength(100)]
    public string HotelName { get; set; } = "";

    [Range(1, 100)]
    public int MaxGuests { get; set; } = 10;

    public int CancellationWindowHours { get; set; } = 24;
}
```

The class **must** be `partial`. It cannot be `static` or `abstract`.

### ConfigurationAttribute Properties

| Property | Default | Description |
|----------|---------|-------------|
| `SectionPath` | Inferred from class name | Configuration section path (e.g., `"Services:OrderApi"`) |
| `ValidateOnStart` | `true` | Generates `.ValidateOnStart()` for immediate startup failure on invalid config |

### Section Path Inference

When `SectionPath` is omitted, it is inferred by removing the `Options` suffix from the class name:

| Class Name | Inferred Section |
|------------|-----------------|
| `BookingOptions` | `"Booking"` |
| `PaymentOptions` | `"Payment"` |
| `MyConfig` | `"MyConfig"` (no suffix to remove) |

Override explicitly when needed:

```csharp
[Configuration(SectionPath = "Services:OrderApi")]
public partial class OrderApiOptions { ... }
```

## Step 3: Provide Configuration Values

In `appsettings.json`:

```json
{
  "Booking": {
    "HotelName": "Grand Hotel",
    "MaxGuests": 50,
    "CancellationWindowHours": 48
  }
}
```

## Step 4: Register in IStartupStep

The source generator produces two extension methods:

1. **Per-type**: `AddBookingOptions(services, configuration)` -- binds a single options class.
2. **Per-assembly**: `Add{Prefix}Configuration(services, configuration)` -- calls all individual `Add*Options` methods in the assembly.

```csharp
[StartupStep]
public class AppStartupStep : IStartupStep
{
    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // One line registers ALL [Configuration] options in the assembly
        services.AddMyAppConfiguration(configuration);
    }
}
```

The generated code wires:

```csharp
services.AddOptions<BookingOptions>()
    .Bind(configuration.GetSection("Booking"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

## Step 5: Consume Options

Use standard Microsoft `IOptions<T>` patterns -- the generator handles all the wiring:

```csharp
// Real-time values (recommended for long-lived services)
public class BookingService(IOptionsMonitor<BookingOptions> options)
{
    public int MaxGuests => options.CurrentValue.MaxGuests;
}

// Per-request snapshot
public class BookingHandler(IOptionsSnapshot<BookingOptions> options) { }

// Singleton (reads once at startup)
public class StaticService(IOptions<BookingOptions> options) { }
```

## Supported Validation Attributes

The source generator recognizes these `System.ComponentModel.DataAnnotations` attributes for generating `.ValidateDataAnnotations()`:

- `[Required]`
- `[Range]`
- `[MaxLength]`, `[MinLength]`, `[StringLength]`
- `[RegularExpression]`
- `[EmailAddress]`, `[Phone]`, `[Url]`

Properties with `required` keyword are treated as required even without `[Required]`.

## Step 6: Register Runtime Stores (Optional)

For runtime configuration (dynamic values, hot-reload, tenant overrides), register the configuration system:

```csharp
services.AddPragmaticConfiguration(options =>
{
    options.EnvironmentTag = "eu-west";      // sub-environment tag
    options.MultiTenant.Enabled = true;      // enable tenant-scoped overrides
    options.MultiTenant.FallbackToBase = true; // cascade to base if no tenant value
    options.Cache.DefaultTtl = TimeSpan.FromSeconds(30);
    options.Cache.SecretsTtl = TimeSpan.FromMinutes(5);
});
```

This registers:
- `IConfigurationStore` (default: `InMemoryConfigurationStore`)
- `ISecretStore` (default: `InMemorySecretStore`)
- `ICacheStack` (default: internal `InMemoryConfigurationCacheStack`, uses `CacheCategories.Configuration`)
- `EnvironmentProfile` (resolved from `IHostEnvironment`)
- `ConfigurationResolver` (scoped, cascade resolution with optional tenant context)

The in-memory stores are registered via `TryAdd`, so database or Azure backends replace them by registering first.

## Diagnostics

| ID | Severity | Description |
|----|----------|-------------|
| PRAG2000 | Error | `[Configuration]` class must be declared as `partial` |
| PRAG2001 | Error | `[Configuration]` class cannot be static or abstract |
| PRAG2050 | Warning | Property has `[Required]` but also has a default value |

## Next Steps

- [Stores](/modules/configuration-module/stores/) -- IConfigurationStore, ISecretStore, runtime reconfiguration, hot-reload
