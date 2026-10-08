---
title: "Pragmatic.Endpoints"
description: "Source-generated HTTP endpoints for ASP.NET Core. Declare the shape; the generator writes the plumbing"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Endpoints/README.md
sidebar:
  order: 0
  label: Overview
---
Source-generated HTTP endpoints for ASP.NET Core. Declare the shape; the generator writes the plumbing
(binding, DI, authorization, error-to-HTTP mapping, and OpenAPI metadata) at compile time, zero
reflection.

## The Problem

Every ASP.NET Core endpoint repeats the same ceremony: parse route params, bind the body, validate,
check authorization, call business logic, map errors to status codes, configure OpenAPI. Minimal APIs
make you write it by hand; Controllers inherit it but cost flexibility. Either way the
plumbing-to-logic ratio grows with every endpoint.

```csharp
// Without Pragmatic: 40+ lines of plumbing for a simple GET
app.MapGet("/api/products/{id}", async (Guid id, IProductRepository repo,
    IAuthorizationService auth, HttpContext ctx, CancellationToken ct) =>
{
    if (!(await auth.AuthorizeAsync(ctx.User, "products.read")).Succeeded) return Results.Forbid();
    var product = await repo.GetByIdAsync(id, ct);
    if (product is null) return Results.NotFound(new ProblemDetails { /* ... */ });
    return Results.Ok(ProductDto.FromEntity(product));
})
.WithName("GetProduct").WithTags("Products").Produces<ProductDto>(200).ProducesProblem(404);
```

## The Solution

You declare **what** the endpoint does; the generator handles **how**.

```csharp
[Endpoint(HttpVerb.Get, "/api/products/{id}")]
[RequirePermission("products.read")]
public partial class GetProduct : Endpoint<ProductDto, NotFoundError>
{
    private IProductRepository _products = null!;       // injected by the generator

    [FromRoute] public Guid Id { get; set; }

    public override async Task<Result<ProductDto, NotFoundError>> HandleAsync(CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(Id, ct);
        return product is not null
            ? ProductDto.FromEntity(product)
            : NotFoundError.For("Product", Id);              // → 404 ProblemDetails
    }
}
```

The generator produces the route registration, parameter binding, DI wiring, authorization
enforcement, error-to-HTTP mapping, and OpenAPI metadata, all at compile time.

## Installation

```bash
dotnet add package Pragmatic.Endpoints
dotnet add package Pragmatic.Endpoints.AspNetCore   # ASP.NET Core hosting integration
dotnet add package Pragmatic.SourceGenerator          # the unified analyzer
```

(Building inside this monorepo? See [Monorepo Structure](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/howto/monorepo-structure.md).)

## Quick Start

Pick the base class that matches the endpoint's result:

| Base class | For | Returns |
|------------|-----|---------|
| `VoidEndpoint` | Fire-and-forget commands | `VoidResult` → 204 |
| `Endpoint<TResponse>` | Always-succeeds reads | `TResponse` → 200 |
| `Endpoint<TResponse, TError>` | Operations that can fail | `Result<TResponse, TError>` → 200 / mapped error |

```csharp
[Endpoint(HttpVerb.Post, "/api/cache/clear")]
public partial class ClearCacheEndpoint : VoidEndpoint
{
    private ICacheStack _cache = null!;                 // injected

    public override async Task<VoidResult> HandleAsync(CancellationToken ct)
    {
        await _cache.InvalidateByTagAsync("products", ct);
        return VoidResult.Success();                    // → 204 No Content
    }
}
```

Properties bind from the request automatically (route param `{id}` → `Id`); typed errors map to the
right status + an RFC 7807 ProblemDetails. The generator emits `MapEndpoint()` (route registration),
`SetDependencies()` (DI), and the OpenAPI metadata. Full walkthrough:
[Getting Started](/modules/endpoints/getting-started/).

## What you can declare

All of these are attribute/convention-driven and documented in depth (see Documentation):

- **Binding** from route, query, header, body, form, and claims (`[FromRoute]`, `[FromQuery]`, …).
- **Endpoint groups** for a shared prefix, tags, and auth.
- **Authorization** via `[RequirePermission]` / `[RequirePolicy]`.
- **API versioning** (`HandleAsyncV2` convention, `[SinceVersion]`, versioned body DTOs).
- **Rate limiting** (inline or named policies, 4 strategies, distributed) and **response caching**.
- **File upload/download** (`IFormFile`, `FileResponse`, ETag, range requests).
- **Pre/post processors**, and direct **DomainAction / Mutation / Query** endpoint integration.

## Status

**Functional** within 1.0.0-alpha: the endpoint pipeline, binding, groups, versioning, rate limiting,
caching, and the DomainAction/Query/Mutation integration. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

## Documentation

### Learn

| Guide | What you will learn |
|-------|---------------------|
| [Architecture and Concepts](/modules/endpoints/concepts/) | Mental model, pipeline lifecycle, decision tree for endpoint types |
| [Getting Started](/modules/endpoints/getting-started/) | First endpoint from zero to HTTP response in 5 minutes |

### Features

| Guide | What it covers |
|-------|----------------|
| [Binding Reference](/modules/endpoints/binding-reference/) | Route, query, header, body, form, claim binding: all scenarios |
| [Endpoint Groups](/modules/endpoints/endpoint-groups/) | Shared route prefix, tags, auth, nesting |
| [Endpoint Processors](/modules/endpoints/processors/) | Pre/post processor pipeline, ordering, short-circuit |
| [API Versioning](/modules/endpoints/versioning/) | HandleAsyncV2 convention, SinceVersion, versioned body DTOs |
| [Rate Limiting](/modules/endpoints/rate-limiting/) | Inline, named policies, all 4 strategies, distributed |
| [Response Caching](/modules/endpoints/response-caching/) | Output cache, VaryBy, distributed via Pragmatic.Caching |
| [File Upload and Download](/modules/endpoints/file-upload-download/) | IFormFile, validation, FileResponse, ETag, range requests |
| [Error Handling](/modules/endpoints/error-handling/) | Result types, ProblemDetails RFC 7807, custom errors |
| [DomainAction Integration](/modules/endpoints/domain-action-integration/) | DomainAction pipeline, invoker, convention versioning |
| [Query and Mutation Endpoints](/modules/endpoints/query-mutation-endpoints/) | Query filters, Mutation CRUD, pagination, autocomplete |
| [Idempotency](/modules/endpoints/idempotency/) | `[Idempotent]`: safe retries with Idempotency-Key, replay, caching rules |
| [SSE Streaming](/modules/endpoints/sse-streaming/) | `StreamingEndpoint`, typed mid-stream errors, backpressure, heartbeat |
| [Request Constraints](/modules/endpoints/request-constraints/) | `[MaxBodySize]`, antiforgery, OpenAPI examples, HEAD/OPTIONS |
| [Generated Response Writers](/modules/endpoints/response-writers/) | JSON responses written by a generated UTF-8 writer, when it is used, what keeps the serializer |
| [MCP Tools](/modules/endpoints/mcp/) | `[McpTool]`: expose endpoints to AI agents via Model Context Protocol |

### Help

| Guide | When to use |
|-------|-------------|
| [Common Mistakes](/modules/endpoints/common-mistakes/) | Wrong code → right code for the most common pitfalls |
| [Troubleshooting](/modules/endpoints/troubleshooting/) | Checklists for 404s, binding issues, auth failures, diagnostics |

## Samples

[Pragmatic.Endpoints.Samples](https://github.com/pragmatic-design/Pragmatic.Design/tree/main/Pragmatic.Endpoints/samples) (17 endpoint patterns) and the Showcase
([Booking](https://github.com/pragmatic-design/Pragmatic.Design/tree/main/examples/showcase/src/Showcase.Booking) ·
[Billing](https://github.com/pragmatic-design/Pragmatic.Design/tree/main/examples/showcase/src/Showcase.Billing) ·
[Catalog](https://github.com/pragmatic-design/Pragmatic.Design/tree/main/examples/showcase/src/Showcase.Catalog)) for real-world CRUD, groups, auth, file upload,
versioning, and cross-boundary events.

## Requirements

- .NET 10.0+
- ASP.NET Core 10.0+ (for `Pragmatic.Endpoints.AspNetCore`)
- `Pragmatic.SourceGenerator` analyzer

## License

Part of the [Pragmatic.Design](/modules/endpoints/overview/) ecosystem. See [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Endpoints is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
