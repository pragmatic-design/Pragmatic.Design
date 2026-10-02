---
title: "Pragmatic.Configuration"
description: "Zero-boilerplate configuration binding, validation, and DI registration via source generator — plus"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Configuration/README.md
sidebar:
  order: 0
  label: Overview
---
Zero-boilerplate configuration binding, validation, and DI registration via source generator — plus
runtime stores with cascade resolution, multi-tenancy, hot-reload, and pluggable backends.

## The Problem

Every `IOptions<T>` class needs the same binding/validation/registration boilerplate. Across 20 options
classes that's hundreds of lines of ceremony, and validation is opt-in: forget one
`.ValidateDataAnnotations()` and `[Required]` is silently ignored until it fails at runtime.

```csharp
// 4 lines of ceremony per options class, repeated for every config type
services.AddOptions<BookingOptions>()
    .Bind(configuration.GetSection("Booking"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

Beyond that: secrets vary by environment, dynamic reconfiguration needs a deploy, and per-tenant
overrides become hardcoded switch statements.

## The Solution — two pillars

**Compile-time binding:** annotate a class with `[Configuration]`; the generator produces the binding,
DataAnnotation validation (on start, by default), and DI registration; the Pragmatic host calls that
registration itself.

```csharp
[Configuration]
public partial class BookingOptions
{
    [Required] public string CurrencyCode { get; init; } = "";
    [Range(1, 365)] public int MaxStayDays { get; init; }
}
```

**Runtime stores** (opt-in, `AddPragmaticConfiguration()`): a unified API over configuration with
cascade resolution — user → tenant → environment → base, highest first; the tenant layer only with
`MultiTenant.Enabled` — hot-reload, and pluggable backends (Database, Azure App Configuration, Redis,
Consul, Kubernetes), without redeploying. Secrets live in an `ISecretStore` (Database, Azure Key Vault,
AWS, GCP, Vault, Kubernetes), and a `secret://{key}` value is resolved from it at read time.

## Installation

```bash
dotnet add package Pragmatic.Configuration
dotnet add package Pragmatic.SourceGenerator   # generates the [Configuration] binding
```

## Status

`[Configuration]` binding/validation and the runtime stores (cascade, hot-reload, the backends above)
are functional within 1.0.0-alpha. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

| [Concepts](/modules/configuration-module/concepts/) | The two pillars, cascade resolution, hot-reload, choosing a backend |
| [Getting Started](/modules/configuration-module/getting-started/) | Your first `[Configuration]` class and a runtime store |
| [Stores](/modules/configuration-module/stores/) | Backends (Database, Azure), cascade, tenant overrides, hot-reload |
| [Common Mistakes](/modules/configuration-module/common-mistakes/) | The most frequent configuration pitfalls |
| [Troubleshooting](/modules/configuration-module/troubleshooting/) | Problem/solution guide with diagnostics |

## Requirements

- .NET 10.0+
- `Pragmatic.SourceGenerator` analyzer

## License

Part of the [Pragmatic.Design](/modules/configuration-module/overview/) ecosystem — see [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Configuration is **MIT-licensed**.
