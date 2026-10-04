---
title: "Architecture and Core Concepts"
description: "This guide explains **why** Pragmatic.Endpoints exists, how its pieces fit together, and how to choose the right abstraction for each situation. Read this befor"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Endpoints/docs/concepts.md
sidebar:
  order: 1
---
This guide explains **why** Pragmatic.Endpoints exists, how its pieces fit together, and how to choose the right abstraction for each situation. Read this before diving into the individual feature guides.

---

## The Problem

ASP.NET Core ships two approaches for building HTTP APIs. Both require significant ceremony and leave important concerns to the developer's discipline.

### Controllers: verbose and ceremony-heavy

```csharp
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class GuestsController : ControllerBase
{
    private readonly IReadRepository<Guest> _guests;
    private readonly IValidator<CreateGuestRequest> _validator;
    private readonly ILogger<GuestsController> _logger;

    public GuestsController(
        IReadRepository<Guest> guests,
        IValidator<CreateGuestRequest> validator,
        ILogger<GuestsController> logger)
    {
        _guests = guests;
        _validator = validator;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GuestDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var guest = await _guests.GetByIdAsync(id, ct);
        if (guest is null)
            return NotFound(new ProblemDetails
            {
                Type = "https://httpstatuses.io/404",
                Title = "Not Found",
                Status = 404,
                Detail = $"Guest '{id}' was not found."
            });

        return Ok(GuestDto.FromEntity(guest));
    }

    [HttpPost]
    [ProducesResponseType(typeof(Guid), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateGuestRequest request, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return UnprocessableEntity(new ProblemDetails { /* ... */ });

        // Create entity, persist, return...
    }
}
```

A single controller with two endpoints: 50+ lines. Most of that code is boilerplate -- constructor injection, attribute ceremony, manual ProblemDetails construction, response type annotations for OpenAPI. None of it is business logic.

### Minimal APIs: no structure, no conventions

```csharp
app.MapGet("/api/v1/guests/{id}", async (Guid id, IReadRepository<Guest> guests, CancellationToken ct) =>
{
    var guest = await guests.GetByIdAsync(id, ct);
    return guest is not null
        ? Results.Ok(GuestDto.FromEntity(guest))
        : Results.Problem(statusCode: 404, detail: $"Guest '{id}' not found.");
})
.WithName("GetGuest")
.WithTags("Guests")
.Produces<GuestDto>(200)
.ProducesProblem(404)
.RequireAuthorization();
```

Compact, but the handler grows linearly with cross-cutting concerns. Add validation, authorization checks, audit logging, rate limiting -- each one interleaves with business logic. With 100 endpoints, these patterns drift apart because there is no enforced structure.

**The fundamental issue**: both approaches force the developer to manually wire together concerns that the framework already has enough information to automate.

---

## The Solution

Pragmatic.Endpoints inverts the model. You declare the endpoint's **shape** -- route, HTTP verb, input properties, response type, error types -- and the source generator produces the exact handler code at compile time.

The same "get guest by ID" endpoint:

```csharp
[Endpoint(HttpVerb.Get, "/{id}")]
[EndpointGroup<GuestsGroup>]
[RequirePermission("booking.guest.read")]
[ApiSummary("Get Guest")]
[ApiTags("Guests")]
public partial class GetGuestEndpoint : Endpoint<GuestDto>
{
    private IReadRepository<Guest> _guests = null!;

    [FromRoute]
    public Guid Id { get; set; }

    public override async Task<Result<GuestDto>> HandleAsync(CancellationToken ct = default)
    {
        var guest = await _guests.GetByIdAsync(Id, ct).ConfigureAwait(false);
        if (guest is null)
            return Result<GuestDto>.Failure(NotFoundError.For<Guest, Guid>(Id));

        return GuestDto.FromEntity(guest);
    }
}
```

The source generator reads this class at compile time and produces:
- A `MapEndpoint()` static method with the route handler lambda
- Parameter binding (route, query, header, body, claims) without reflection
- A `SetDependencies()` method that injects `_guests` from the DI container
- OpenAPI metadata: response types, status codes, tags, summary
- ProblemDetails error mapping for every `IError` in the return type
- Rate limiting, authorization, and caching middleware when declared

No reflection at runtime. No runtime code generation. The generated code is visible in your IDE under the `obj/` directory, fully debuggable.

---

## How It Works: The Request Pipeline

Every request to a Pragmatic endpoint flows through a deterministic pipeline. The source generator assembles the pipeline at compile time based on the attributes and base class you choose.

```
HTTP Request
  |
  v
Route Matching                     ASP.NET Core router
  |
  v
Parameter Binding                  SG-generated: route, query, header, body, claims
  |
  v
Pre-Processors                    [PreProcessor<T>] in Order
  |                               Can short-circuit with error
  v
File Validation                    [MaxFileSize], [AllowedContentTypes]
  |                               Only for [FromForm] IFormFile
  v
Input Validation                   [Validate] --> ISyncValidator / IAsyncValidator
  |                               Returns ValidationError on failure
  v
Handler                            HandleAsync (Endpoint)
  |                               Execute (DomainAction)
  |                               ApplyAsync (Mutation)
  v
Post-Processors                   [PostProcessor<T>] in Order
  |                               Always runs (success or failure)
  v
Result Mapping                     Result<T,E> --> HTTP status + ProblemDetails
  |
  v
HTTP Response
```

The pipeline steps that apply depend on the base class and attributes. A plain `Endpoint<T>` with no processors or validation skips straight from binding to the handler. A `DomainAction<T>` with `[Validate]`, `[PreProcessor]`, and `[PostProcessor]` gets the full pipeline. The SG only generates the steps that are declared -- there is no performance penalty for unused features.

---

## Choosing the Right Base Class

The framework provides four base classes, each designed for a specific level of complexity. The decision tree below helps you pick the right one.

### Decision Table

| Question | If Yes | If No |
|----------|--------|-------|
| Simple request/response with direct logic? | `Endpoint<T>` | Keep reading |
| No response body needed (fire-and-forget, delete)? | `VoidEndpoint` | Keep reading |
| Need validation + authorization + DI pipeline for business logic? | `DomainAction<T>` or `VoidDomainAction` | Keep reading |
| CRUD operation on a persisted entity? | `Mutation<T>` | Keep reading |
| Query with filters, sorting, pagination, projection? | `Query<TEntity, TResult>` | Use `Endpoint<T>` |

### Base Class Reference

| Base Class | Handler Method | Return Type | Pipeline | Typical HTTP Verbs |
|------------|---------------|-------------|----------|-------------------|
| `Endpoint<T>` | `HandleAsync` | `Result<T>` or `Result<T, TError>` | Binding + Processors | GET, POST |
| `VoidEndpoint` | `HandleAsync` | `VoidResult` | Binding + Processors | DELETE, PUT |
| `DomainAction<T>` | `Execute` | `Result<T, IError>` | Full action pipeline (validation, filters, invoker) | POST, PUT |
| `VoidDomainAction` | `Execute` | `VoidResult<IError>` | Full action pipeline | DELETE, POST |
| `Mutation<T>` | `ApplyAsync` | `Result<T, IError>` | Entity load, setter mapping, persist | POST, PUT, DELETE |
| `Query<T, R>` | (generated) | `PagedResult<R>` | Filter, sort, page, project | GET |

### When NOT to Use Each

**Do not use `DomainAction<T>` for trivial lookups.** The invoker pipeline adds overhead (filter chain, observability, activity tracing) for no benefit. A `GetGuestEndpoint : Endpoint<GuestDto>` is the right choice for "load by ID and return DTO."

**Do not use raw `Endpoint<T>` when you need cross-cutting concerns.** If three endpoints all need the same validation, authorization check, and audit trail, you will duplicate that code across all three handlers. Use `DomainAction<T>` and let the pipeline handle it, or attach processors.

**Do not use `Mutation<T>` for read operations.** Mutations are tied to the persistence unit-of-work and call `SaveChangesAsync` at the end. Queries should use `Query<T, R>` or `Endpoint<T>`.

**Do not use `Query<T, R>` for complex multi-step reads.** If your read requires multiple round-trips, aggregation across entities, or stateful computation, use `Endpoint<T>` with manual repository calls. The Showcase's `SearchAvailableRoomsEndpoint` is a good example -- it counts overlapping reservations per room type, which cannot be expressed as a single IQueryable projection.

---

## Endpoint Registration Model

Three calls connect your endpoints to the ASP.NET Core pipeline.

### 1. `[Endpoint]` attribute -- compile time

Marks a class for source generation. The SG produces a static `MapEndpoint()` method on the class.

```csharp
[Endpoint(HttpVerb.Get, "/api/v1/guests/{id}")]
public partial class GetGuestEndpoint : Endpoint<GuestDto> { /* ... */ }
```

### 2. `AddPragmaticEndpoints()` -- service registration

Registers all generated endpoint services (handlers, processors, invokers) in the DI container.

### 3. `MapPragmaticEndpoints()` -- route registration

Maps all generated routes to the ASP.NET Core endpoint router.

### With Pragmatic.Composition (recommended)

When you use `Pragmatic.Composition` and `PragmaticApp.RunAsync()`, both calls are **automatic**; the SG-generated host handles everything:

```csharp
// Program.cs: with Composition, no manual registration needed
await PragmaticApp.RunAsync(args, app =>
{
    app.UseAuthentication<NoOpAuthenticationHandler>("Default");
});
// AddPragmaticEndpoints() and MapPragmaticEndpoints() are called by the generated host
```

### Without Composition (standalone)

If you use Endpoints without Composition, you call them manually in `Program.cs`:

```csharp
// Program.cs: standalone, manual registration
builder.Services.AddPragmaticEndpoints(options =>
{
    options.RoutePrefix = "/api";
});

var app = builder.Build();
app.MapPragmaticEndpoints();
app.Run();
```

### How groups, prefixes, and global options compose

Route composition stacks three layers. Each layer is optional.

```
Global RoutePrefix (PragmaticEndpointsOptions)
  + Group RoutePrefix ([EndpointGroup])
    + Endpoint Route ([Endpoint])
    = Final Route
```

Example: global prefix `/api`, group prefix `/v1/reservations`, endpoint route `/{id}/cancel` produces `/api/v1/reservations/{id}/cancel`.

The SG generates a `MapGroup()` call for each group, so grouped endpoints share ASP.NET Core's route group middleware (authorization, rate limiting, CORS) automatically.

---

## Parameter Binding Model

The source generator infers how to bind each property based on attributes and conventions. No reflection at runtime -- the binding code is generated as a plain lambda parameter list.

### Binding inference rules

| Rule | How the SG decides | Example |
|------|-------------------|---------|
| **Route** | `[FromRoute]`, or property name matches a `{parameter}` in the route template | `public Guid Id { get; set; }` with route `/{id}` |
| **Header** | `[FromHeader]` with explicit header name | `[FromHeader(Name = "X-Idempotency-Key")]` |
| **Query** | `[FromQuery]`, or properties on GET endpoints not matched by other rules | `[FromQuery] public string? Category { get; set; }` |
| **Claim** | `[FromClaim("sub")]` -- extracted from `HttpContext.User` at runtime | `[FromClaim("sub")] public Guid UserId { get; set; }` |
| **Form** | `[FromForm]` -- for file uploads (`IFormFile`) | `[FromForm] public required IFormFile File { get; set; }` |
| **Body** | Remaining `public` properties with no binding attribute | Grouped into a generated Body DTO record |

### Generated Body DTO

When an endpoint has properties that are not bound to route, query, header, claim, or form, the SG creates a record DTO class:

```csharp
// You write:
[Endpoint(HttpVerb.Post, "api/v1/guests")]
public partial class CreateGuestMutation : Mutation<Guest>
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public string? Phone { get; init; }
}

// SG generates: CreateGuestMutationBody.RequestBody.g.cs
public sealed record CreateGuestMutationBody
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public string? Phone { get; init; }
}
```

The generated handler deserializes the request body into this DTO, then maps the properties onto the endpoint instance.

---

## Error Handling Model

Pragmatic.Endpoints uses the Result pattern to make error paths explicit and type-safe. Every possible error type is declared in the class signature, and the SG generates the correct HTTP status mapping.

### Standard error-to-status mapping

| Return Type | HTTP Status |
|-------------|-------------|
| `Result<T>` success | 200 OK (or custom via `[HttpStatus]`) |
| `VoidResult` success | 204 No Content |
| `NotFoundError` | 404 Not Found |
| `ValidationError` | 400 Bad Request |
| `BadRequestError` | 400 Bad Request |
| `UnauthorizedError` | 401 Unauthorized |
| `ForbiddenError` | 403 Forbidden |
| `ConflictError` | 409 Conflict |
| Other `IError` | 422 Unprocessable Entity |

### Custom errors

Apply `[HttpStatus]` to map a domain error to a specific HTTP status:

```csharp
[HttpStatus(402)]
public record PaymentRequiredError(string Title) : IError
{
    public string Code => "PAYMENT_REQUIRED";
}
```

### ProblemDetails output

All errors are serialized as RFC 7807 ProblemDetails, served as `application/problem+json` whatever the status. The SG generates the mapping code -- you never construct ProblemDetails manually.

```json
{
  "type": "https://httpstatuses.io/404",
  "title": "Not Found",
  "status": 404,
  "detail": "Guest with ID '3fa85f64-5717-4562-b3fc-2c963f66afa6' was not found.",
  "instance": "/api/v1/guests/3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

### Multiple error types

Declare all possible errors in the generic parameters. The SG generates OpenAPI response schemas for each:

```csharp
public partial class CancelReservationMutation : Mutation<Reservation, ConflictError>
// Generated OpenAPI: 200 (Reservation), 404 (entity not found), 409 (ConflictError)
```

---

## What Gets Generated

For each class marked with `[Endpoint]`, the source generator produces one or more files. The exact set depends on the class's properties, base class, and attributes.

| Generated File | Content | Condition |
|----------------|---------|-----------|
| `{Type}.Endpoint.g.cs` | `MapEndpoint` static method with the handler lambda, parameter binding, result mapping, OpenAPI metadata | Always |
| `{Type}Body.RequestBody.g.cs` | Body DTO record with `[FromBody]` properties | If body properties exist |
| `{Type}.SetDependencies.g.cs` | DI property injection method for private fields | If private fields with `= null!` exist |
| `{Type}V{N}Body.RequestBody.g.cs` | Versioned body DTOs for `[SinceVersion]` properties | If `[SinceVersion]` is used |
| `_Infra.Endpoints.Registration.g.cs` | `MapPragmaticEndpoints()` method listing all endpoints and groups | Once per assembly |

### Example: generated handler for GetGuestEndpoint

The SG transforms the 20-line class you write into a Minimal API handler that includes route binding, DI resolution, `SetDependencies` call, `HandleAsync` invocation, and `Result.Match` for status mapping -- approximately 40 lines of generated code that you never maintain.

All generated files live under `obj/Debug/net10.0/generated/` and are fully visible in the IDE. You can set breakpoints in generated code.

---

## Ecosystem Integration Overview

Pragmatic.Endpoints is the HTTP surface layer. Other Pragmatic modules plug into it to provide deeper pipeline features. Each integration is opt-in: reference the package, and the SG detects it.

### Actions

When a class inherits `DomainAction<T>` or `VoidDomainAction` and has `[Endpoint]`, the SG generates a handler that resolves `IDomainActionInvoker<T>` from DI and calls `InvokeAsync`. This enables the full action pipeline: validation filters, action filters, observability tracing, boundary interfaces. Use this for commands and complex business operations.

### Persistence

`Mutation<T>` endpoints integrate with the persistence unit-of-work. The SG generates code that loads the entity by ID, maps input properties via generated setters, calls `ApplyAsync`, and persists via `SaveChangesAsync`. `Query<TEntity, TResult>` endpoints generate filter/sort/page/project pipelines over `IQueryable`. Use these for standard CRUD.

### Validation

The `[Validate]` attribute triggers automatic input validation before the handler runs. The SG detects validation attributes (`[Required]`, `[Email]`, `[MinLength]`, etc.) on endpoint properties and generates sync validators. For complex validation, implement `IAsyncValidator<T>`. Validation errors short-circuit the pipeline and return 400.

### Authorization

`[RequirePermission("booking.guest.read")]` and `[RequirePolicy<T>]` attach authorization metadata to the generated endpoint. The authorization filter runs before the handler. `[AllowAnonymous]` overrides group-level authorization for public endpoints.

### Caching

`[ResponseCache(Duration = 60)]` generates output caching middleware on the endpoint. `[Cacheable]` from `Pragmatic.Caching` integrates with `HybridCache` for query result caching with tag-based invalidation. Both are applied at the endpoint group or individual endpoint level.

### Mapping

`Query<TEntity, TResult>` endpoints use SG-generated projection mappings (from `[MapFrom<T>]` on the DTO) to project entities to DTOs at the IQueryable level. This ensures only the needed columns are fetched from the database.

---

## See Also

- [Getting Started](/modules/endpoints/getting-started/) -- Install and create your first endpoint in 5 minutes
- [Binding Reference](/modules/endpoints/binding-reference/) -- All binding scenarios (route, query, header, body, claim, form)
- [Error Handling](/modules/endpoints/error-handling/) -- Custom errors, ProblemDetails, multiple error types
- [DomainAction Integration](/modules/endpoints/domain-action-integration/) -- Full action pipeline with invoker
- [Query and Mutation Endpoints](/modules/endpoints/query-mutation-endpoints/) -- CRUD with filters, sorting, paging
- [Endpoint Groups](/modules/endpoints/endpoint-groups/) -- Shared route prefixes, tags, authorization
- [Processors](/modules/endpoints/processors/) -- Pre/post processing hooks
- [Versioning](/modules/endpoints/versioning/) -- API versioning with `[SinceVersion]` and `ExecuteV{N}`
- [Showcase: Booking endpoints](https://github.com/pragmatic-design/Pragmatic.Design/tree/main/examples/showcase/src/Showcase.Booking) -- Real-world examples
