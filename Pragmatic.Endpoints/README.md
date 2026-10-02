# Pragmatic.Endpoints

Source-generated HTTP endpoints for ASP.NET Core. Declare the shape; the generator writes the plumbing
— binding, DI, authorization, error-to-HTTP mapping, and OpenAPI metadata — at compile time, zero
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
enforcement, error-to-HTTP mapping, and OpenAPI metadata — all at compile time.

## Installation

```bash
dotnet add package Pragmatic.Endpoints
dotnet add package Pragmatic.Endpoints.AspNetCore   # ASP.NET Core hosting integration
dotnet add package Pragmatic.SourceGenerator          # the unified analyzer
```

(Building inside this monorepo? See [Monorepo Structure](../docs/howto/monorepo-structure.md).)

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
[Getting Started](docs/getting-started.md).

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

**Functional** within 1.0.0-alpha — the endpoint pipeline, binding, groups, versioning, rate limiting,
caching, and the DomainAction/Query/Mutation integration. See the [roadmap](../docs/ROADMAP.md).

## Documentation

### Learn

| Guide | What you will learn |
|-------|---------------------|
| [Architecture and Concepts](docs/concepts.md) | Mental model, pipeline lifecycle, decision tree for endpoint types |
| [Getting Started](docs/getting-started.md) | First endpoint from zero to HTTP response in 5 minutes |

### Features

| Guide | What it covers |
|-------|----------------|
| [Binding Reference](docs/binding-reference.md) | Route, query, header, body, form, claim binding — all scenarios |
| [Endpoint Groups](docs/endpoint-groups.md) | Shared route prefix, tags, auth, nesting |
| [Endpoint Processors](docs/processors.md) | Pre/post processor pipeline, ordering, short-circuit |
| [API Versioning](docs/versioning.md) | HandleAsyncV2 convention, SinceVersion, versioned body DTOs |
| [Rate Limiting](docs/rate-limiting.md) | Inline, named policies, all 4 strategies, distributed |
| [Response Caching](docs/response-caching.md) | Output cache, VaryBy, distributed via Pragmatic.Caching |
| [File Upload and Download](docs/file-upload-download.md) | IFormFile, validation, FileResponse, ETag, range requests |
| [Error Handling](docs/error-handling.md) | Result types, ProblemDetails RFC 7807, custom errors |
| [DomainAction Integration](docs/domain-action-integration.md) | DomainAction pipeline, invoker, convention versioning |
| [Query and Mutation Endpoints](docs/query-mutation-endpoints.md) | Query filters, Mutation CRUD, pagination, autocomplete |
| [Idempotency](docs/idempotency.md) | `[Idempotent]` — safe retries with Idempotency-Key, replay, caching rules |
| [SSE Streaming](docs/sse-streaming.md) | `StreamingEndpoint`, typed mid-stream errors, backpressure, heartbeat |
| [Request Constraints](docs/request-constraints.md) | `[MaxBodySize]`, antiforgery, OpenAPI examples, HEAD/OPTIONS |
| [MCP Tools](docs/mcp.md) | `[McpTool]` — expose endpoints to AI agents via Model Context Protocol |

### Help

| Guide | When to use |
|-------|-------------|
| [Common Mistakes](docs/common-mistakes.md) | Wrong code → right code for the most common pitfalls |
| [Troubleshooting](docs/troubleshooting.md) | Checklists for 404s, binding issues, auth failures, diagnostics |

## Samples

[Pragmatic.Endpoints.Samples](samples/) (17 endpoint patterns) and the Showcase
([Booking](../examples/showcase/src/Showcase.Booking/) ·
[Billing](../examples/showcase/src/Showcase.Billing/) ·
[Catalog](../examples/showcase/src/Showcase.Catalog/)) for real-world CRUD, groups, auth, file upload,
versioning, and cross-boundary events.

## Requirements

- .NET 10.0+
- ASP.NET Core 10.0+ (for `Pragmatic.Endpoints.AspNetCore`)
- `Pragmatic.SourceGenerator` analyzer

## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.Endpoints is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
