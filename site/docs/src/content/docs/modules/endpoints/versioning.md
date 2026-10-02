---
title: "API Versioning"
description: "APIs evolve. You add fields, change behavior, deprecate features. But existing clients depend on the current contract. Breaking changes cause client failures. A"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Endpoints/docs/versioning.md
sidebar:
  order: 16
---
## The Problem

APIs evolve. You add fields, change behavior, deprecate features. But existing clients depend on the current contract. Breaking changes cause client failures. API versioning solves this by allowing multiple versions of an endpoint to coexist, each with its own request/response shape, while clients migrate at their own pace.

The typical approach -- manually maintaining separate controller classes or route handlers for each version -- is tedious and error-prone. Pragmatic.Endpoints provides convention-based versioning: you define versioned handler methods on the same class, and the source generator takes care of route registration, body DTO generation, and API version set configuration.

---

## Convention-Based Versioning

The core idea: instead of creating separate classes for each version, you add versioned methods to the same endpoint class. The SG detects these methods by name pattern and generates versioned routes automatically.

### For `Endpoint<T>`: HandleAsyncV{n}

```csharp
[Endpoint(HttpVerb.Post, "/api/products")]
public partial class CreateProduct : Endpoint<ProductDto>
{
    public required string Name { get; init; }
    public required decimal Price { get; init; }

    [SinceVersion("2.0")]
    public string? Sku { get; init; }

    [SinceVersion("3.0")]
    public string? Category { get; init; }

    // V1: original handler
    public override async Task<Result<ProductDto, ValidationError>> HandleAsync(CancellationToken ct)
    {
        // Handles v1 requests (Name, Price only)
    }

    // V2: added Sku support
    public async Task<Result<ProductDto, ValidationError>> HandleAsyncV2(CancellationToken ct)
    {
        // Handles v2 requests (Name, Price, Sku)
    }

    // V3: added Category
    public async Task<Result<ProductDto, ValidationError>> HandleAsyncV3(CancellationToken ct)
    {
        // Handles v3 requests (Name, Price, Sku, Category)
    }
}
```

### For `DomainAction<T>`: ExecuteV{n}

The same convention applies to DomainAction endpoints:

```csharp
[DomainAction]
[Endpoint(HttpVerb.Post, "/api/orders")]
public partial class PlaceOrder : DomainAction<OrderId>
{
    public required string Product { get; init; }
    public required int Quantity { get; init; }

    [SinceVersion("2.0")]
    public string? CouponCode { get; init; }

    public override async Task<Result<OrderId, OrderError>> Execute(CancellationToken ct)
    {
        // V1 logic
    }

    public async Task<Result<OrderId, OrderError>> ExecuteV2(CancellationToken ct)
    {
        // V2 logic (with coupon support)
    }
}
```

### Method Name Patterns

The SG recognizes these patterns:

| Pattern | Version | Examples |
|---------|---------|----------|
| `HandleAsync` / `Execute` | 1.0.0 | Base version (always included) |
| `HandleAsyncV2` / `ExecuteV2` | 2.0.0 | Major version |
| `HandleAsyncV2_1` / `ExecuteV2_1` | 2.1.0 | Minor version |
| `HandleAsyncV2_1_3` / `ExecuteV2_1_3` | 2.1.3 | Patch version |

The regex patterns used:
- Endpoint: `^HandleAsync(V(\d+)(_(\d+)(_(\d+))?)?)?$`
- DomainAction: `^Execute(V(\d+)(_(\d+)(_(\d+))?)?)?$`

Each method must take a single `CancellationToken` parameter to be recognized.

---

## `[SinceVersion]` on Properties

The `[SinceVersion]` attribute marks when a property was introduced. Properties without `[SinceVersion]` are considered part of version 1.0.

```csharp
public required string Name { get; init; }         // Available in all versions

[SinceVersion("2.0")]
public string? Phone { get; init; }                // Available from V2 onwards

[SinceVersion("3.0")]
public string? Email { get; init; }                // Available from V3 onwards
```

The SG uses this information to generate version-specific body DTOs. For version 2.0, the DTO includes `Name` and `Phone` but not `Email`. For version 3.0, it includes all three.

### Filtering Logic

The SG includes a property in a version's body DTO if:

```
property has no [SinceVersion]  -> included in all versions
property.SinceVersion <= requested version  -> included
property.SinceVersion > requested version   -> excluded
```

Version comparison is major.minor (e.g., `[SinceVersion("2.0")]` is included in versions 2.0, 2.1, 3.0, etc.).

---

## `[ApiVersion]` Attribute (Optional Metadata)

The `[ApiVersion]` attribute is separate from convention-based versioning. It provides metadata about version deprecation and sunset dates, useful for OpenAPI documentation and client communication.

```csharp
[Endpoint(HttpVerb.Get, "/api/orders")]
[ApiVersion("1.0", Deprecated = true, SunsetDate = "2025-06-30")]
[ApiVersion("2.0")]
public partial class GetOrders : Endpoint<OrderListDto> { }
```

| Property | Type | Description |
|----------|------|-------------|
| `Version` | `string` | Version string (e.g., "1.0", "2.0") |
| `Deprecated` | `bool` | Whether this version is deprecated |
| `DeprecationMessage` | `string?` | Human-readable deprecation message |
| `SunsetDate` | `string?` | ISO 8601 date when version will be removed (e.g., "2025-06-30") |

`AllowMultiple = true` -- you can apply multiple `[ApiVersion]` attributes to declare all supported versions.

When a version is marked as deprecated, the SG adds metadata to the endpoint builder:

```csharp
builder.WithMetadata(new Microsoft.AspNetCore.Mvc.ApiVersionAttribute("1.0") { Deprecated = true });
```

This metadata is picked up by OpenAPI generators to mark the endpoint as deprecated in API documentation.

---

## What the SG Generates

When convention-based versioning is detected, the SG generates several things:

### 1. ApiVersionSet

An `Asp.Versioning.ApiVersionSet` that declares all supported versions:

```csharp
var versionSet = endpoints.NewApiVersionSet()
    .HasApiVersion(new Asp.Versioning.ApiVersion(1, 0))
    .HasApiVersion(new Asp.Versioning.ApiVersion(2, 0))
    .HasApiVersion(new Asp.Versioning.ApiVersion(3, 0))
    .Build();
```

### 2. Per-Version Route Handlers

Each version gets its own `Map{Method}()` call, all on the same route but mapped to different API versions:

```csharp
var builderV1 = endpoints.MapPost("/api/products", async (...) =>
{
    // Calls HandleAsync (v1)
});
builderV1.WithApiVersionSet(versionSet).MapToApiVersion(new Asp.Versioning.ApiVersion(1, 0));

var builderV2 = endpoints.MapPost("/api/products", async (...) =>
{
    // Calls HandleAsyncV2 (v2)
});
builderV2.WithApiVersionSet(versionSet).MapToApiVersion(new Asp.Versioning.ApiVersion(2, 0));

var builder = endpoints.MapPost("/api/products", async (...) =>
{
    // Calls HandleAsyncV3 (v3)
});
builder.WithApiVersionSet(versionSet).MapToApiVersion(new Asp.Versioning.ApiVersion(3, 0));
```

The last handler's builder variable is named `builder` (without suffix), and shared endpoint configuration (auth, rate limiting, tags, etc.) is applied to it.

### 3. Versioned Body DTOs

The `VersionedBodyDtoTemplate` generates a separate partial record for each version, containing only the properties available in that version:

```csharp
// Generated: CreateProductV1Body
public partial record CreateProductV1Body
{
    public required string Name { get; init; }
    public required decimal Price { get; init; }
}

// Generated: CreateProductV2Body
public partial record CreateProductV2Body
{
    public required string Name { get; init; }
    public required decimal Price { get; init; }
    public string? Sku { get; init; }
}

// Generated: CreateProductV3Body
public partial record CreateProductV3Body
{
    public required string Name { get; init; }
    public required decimal Price { get; init; }
    public string? Sku { get; init; }
    public string? Category { get; init; }
}
```

The DTO suffix follows the convention `{TypeName}V{Major}Body`, `{TypeName}V{Major}_{Minor}Body`, or `{TypeName}V{Major}_{Minor}_{Patch}Body`.

### 4. Auto-Registration of API Versioning

If any endpoint in the assembly uses convention-based versioning and `Asp.Versioning.Http` is available, the generated `AddPragmaticEndpoints()` includes:

```csharp
services.AddApiVersioning();
```

This is idempotent, so it is safe even if called from multiple assemblies.

### 5. Trace Enrichment

Each version handler enriches the current `Activity` with version metadata:

```csharp
System.Diagnostics.Activity.Current?.SetTag("pragmatic.endpoint.name", "CreateProduct");
System.Diagnostics.Activity.Current?.SetTag("pragmatic.endpoint.version", "2.0");
```

---

## Asp.Versioning.Http Dependency and PRAG0551

Convention-based versioning requires the `Asp.Versioning.Http` NuGet package. The SG checks for the presence of `Asp.Versioning.ApiVersion` in the compilation.

If versioned methods are detected (e.g., `HandleAsyncV2`) but `Asp.Versioning.Http` is not referenced, the SG emits diagnostic **PRAG0551**:

> **PRAG0551**: Versioned methods require Asp.Versioning.Http
>
> '{TypeName}' has versioned methods (ExecuteV2/HandleAsyncV2, etc.) but 'Asp.Versioning.Http' is not referenced. Add the NuGet package to enable versioned endpoint generation. Without it, only the default version endpoint will be generated.

This is a warning, not an error. Without the package, the SG generates only the base (v1) endpoint handler and skips versioned route generation.

To fix:

```xml
<PackageReference Include="Asp.Versioning.Http" Version="8.*" />
```

---

## Practical Example: Evolving a Product Endpoint

### Step 1: Initial Endpoint (v1)

```csharp
[Endpoint(HttpVerb.Post, "/api/products")]
public partial class CreateProduct : Endpoint<ProductDto>
{
    public required string Name { get; init; }
    public required decimal Price { get; init; }

    public override async Task<Result<ProductDto, ValidationError>> HandleAsync(CancellationToken ct)
    {
        var product = new Product { Name = Name, Price = Price };
        await _repository.SaveAsync(product, ct);
        return new ProductDto(product.Id, product.Name, product.Price);
    }
}
```

Clients send: `{ "name": "Widget", "price": 9.99 }`

### Step 2: Add SKU Field (v2)

Requirements change: products now need an optional SKU.

```csharp
[Endpoint(HttpVerb.Post, "/api/products")]
public partial class CreateProduct : Endpoint<ProductDto>
{
    public required string Name { get; init; }
    public required decimal Price { get; init; }

    [SinceVersion("2.0")]
    public string? Sku { get; init; }

    // V1: unchanged -- existing clients keep working
    public override async Task<Result<ProductDto, ValidationError>> HandleAsync(CancellationToken ct)
    {
        var product = new Product { Name = Name, Price = Price };
        await _repository.SaveAsync(product, ct);
        return new ProductDto(product.Id, product.Name, product.Price);
    }

    // V2: uses the new Sku field
    public async Task<Result<ProductDto, ValidationError>> HandleAsyncV2(CancellationToken ct)
    {
        var product = new Product { Name = Name, Price = Price, Sku = Sku };
        await _repository.SaveAsync(product, ct);
        return new ProductDto(product.Id, product.Name, product.Price, product.Sku);
    }
}
```

V1 clients send `{ "name": "Widget", "price": 9.99 }` -- still works.
V2 clients send `{ "name": "Widget", "price": 9.99, "sku": "WDG-001" }`.

Both hit the same route (`/api/products`), differentiated by the API version header/query parameter (as configured by Asp.Versioning).

### Step 3: Deprecate V1

When you want to signal that v1 will be removed:

```csharp
[Endpoint(HttpVerb.Post, "/api/products")]
[ApiVersion("1.0", Deprecated = true, SunsetDate = "2026-01-01")]
[ApiVersion("2.0")]
public partial class CreateProduct : Endpoint<ProductDto> { /* ... */ }
```

OpenAPI documentation will show v1 as deprecated with a sunset date.

---

## Notes

- **Version resolution**: How the client specifies the version (URL prefix, query parameter, header) is configured by `Asp.Versioning.Http`, not by Pragmatic. The SG generates `ApiVersionSet` and `MapToApiVersion()` calls; the resolution strategy is configured at the Asp.Versioning level.
- **Patch versions**: The `HandleAsyncV2_1_3` pattern supports patch versions, but `Asp.Versioning.ApiVersion` only supports major.minor. Patch versions differentiate body DTOs but map to the same Asp.Versioning API version (`2.1`).
- **Base method is always v1**: When versioned methods are detected, the SG automatically includes the base `HandleAsync`/`Execute` as version 1.0.0, even if it is not explicitly annotated.
- **Group-level versioning**: `[EndpointGroup]` has a `Version` property that applies an API version to all endpoints in the group. This is metadata-only and does not create versioned handlers.
- **ActionVersions empty = no versioning**: If no versioned methods are detected (only base `HandleAsync`), the SG generates a single unversioned route as normal.
