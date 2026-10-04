# Common Mistakes

These are the most common issues developers encounter when using Pragmatic.Endpoints. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Forgetting `partial` on the Endpoint Class

**Wrong:**

```csharp
[Endpoint(HttpVerb.Get, "/orders/{id}")]
public class GetOrderEndpoint : Endpoint<OrderDto, NotFoundError>
{
    [FromRoute]
    public Guid Id { get; init; }

    public override async Task<Result<OrderDto, NotFoundError>> HandleAsync(CancellationToken ct)
    {
        // ...
    }
}
```

**Compile result:** `PRAG0500` error -- "Endpoint class 'GetOrderEndpoint' must be declared as partial".

**Right:**

```csharp
[Endpoint(HttpVerb.Get, "/orders/{id}")]
public partial class GetOrderEndpoint : Endpoint<OrderDto, NotFoundError>
{
    [FromRoute]
    public Guid Id { get; init; }

    public override async Task<Result<OrderDto, NotFoundError>> HandleAsync(CancellationToken ct)
    {
        // ...
    }
}
```

**Why:** The source generator emits `SetDependencies`, the handler delegate, and endpoint registration into a partial class. Without `partial`, the compiler cannot merge the generated code with your class.

---

## 2. Missing Route Parameters in Properties

**Wrong:**

```csharp
[Endpoint(HttpVerb.Get, "/guests/{guestId}/reservations/{reservationId}")]
public partial class GetGuestReservationEndpoint : Endpoint<ReservationDto, NotFoundError>
{
    // No properties matching route parameters!

    public override async Task<Result<ReservationDto, NotFoundError>> HandleAsync(CancellationToken ct)
    {
        // guestId and reservationId are never bound -- always default values
    }
}
```

**Compile result:** `PRAG0504` warning -- "Route parameter 'guestId' in endpoint 'GetGuestReservationEndpoint' does not match any public property". The endpoint compiles but both parameters are silently unbound at runtime (always `Guid.Empty` or `null`).

**Right:**

```csharp
[Endpoint(HttpVerb.Get, "/guests/{guestId}/reservations/{reservationId}")]
public partial class GetGuestReservationEndpoint : Endpoint<ReservationDto, NotFoundError>
{
    [FromRoute]
    public Guid GuestId { get; init; }

    [FromRoute]
    public Guid ReservationId { get; init; }

    public override async Task<Result<ReservationDto, NotFoundError>> HandleAsync(CancellationToken ct)
    {
        // GuestId and ReservationId are correctly bound from the URL
    }
}
```

**Why:** The SG matches route template parameters (`{guestId}`) to public properties by name (case-insensitive). If no property matches, the parameter is never populated. Always check `PRAG0504` warnings in the build output.

---

## 3. Leaving Form Fields Unmarked Next to a File

⚠️ **This is not a mistake, though it looks like one.** An operation that carries a file is `multipart/form-data`, so there is
no JSON body for an unmarked property to arrive in: the generator binds every scalar from the form,
marked or not, under its property name. The two declarations below are equivalent; the second is
explicit, which is why the framework's own examples write it. What a form field genuinely cannot carry
is a nested object, and that is reported as `PRAG0552`.

**Implicit:**

```csharp
[Endpoint(HttpVerb.Post, "/invoices/{invoiceId}/attachments")]
public partial class UploadInvoiceAttachmentEndpoint : Endpoint<AttachmentDto>
{
    [FromRoute]
    public Guid InvoiceId { get; init; }

    // IFormFile without [FromForm] on the other fields
    public IFormFile Document { get; set; } = null!;
    public string Description { get; set; } = "";  // Bound from the form
}
```

**Result:** `Description` arrives as a form field, like the file beside it.

**Explicit, and preferred in the examples:**

```csharp
[Endpoint(HttpVerb.Post, "/invoices/{invoiceId}/attachments")]
public partial class UploadInvoiceAttachmentEndpoint : Endpoint<AttachmentDto>
{
    [FromRoute]
    public Guid InvoiceId { get; init; }

    [FromForm]
    [MaxFileSize(25 * 1024 * 1024)]
    [AllowedContentTypes("application/pdf", "image/jpeg", "image/png")]
    public IFormFile Document { get; set; } = null!;

    [FromForm]
    public string Description { get; set; } = "";
}
```

**Why:** An endpoint is either JSON body or multipart form, not both, and the file decides. The SG
generates no body DTO for a multipart operation (whether or not every property is marked), binds the
scalars from the form, emits `DisableAntiforgery()` automatically, and reports `PRAG0552` for a
property a form field cannot carry.

---

## 4. Calling MapPragmaticEndpoints Before AddPragmaticEndpoints

**Wrong:**

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapPragmaticEndpoints();  // Options singleton not registered yet!

builder.Services.AddPragmaticEndpoints(options =>
{
    options.RoutePrefix = "/api";
    options.EnableOpenApi = true;
});
```

**Runtime result:** `InvalidOperationException` at startup -- the `PragmaticEndpointsOptions` singleton is not registered when `MapPragmaticEndpoints` tries to resolve it. In some cases, it silently uses default options, ignoring your `RoutePrefix` and `EnableOpenApi` settings.

**Right:**

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. Register services BEFORE building
builder.Services.AddPragmaticEndpoints(options =>
{
    options.RoutePrefix = "/api";
    options.EnableOpenApi = true;
});

var app = builder.Build();

// 2. Map endpoints AFTER building
app.MapPragmaticEndpoints();

app.Run();
```

**Why:** `AddPragmaticEndpoints` registers the `PragmaticEndpointsOptions` singleton into the DI container. `MapPragmaticEndpoints` reads those options at startup to apply route prefixes, OpenAPI metadata, and group configuration. The service registration must happen before `Build()`, and endpoint mapping must happen after.

---

## 5. Using DomainAction for Simple Lookups

**Wrong:**

```csharp
[DomainAction]
[Endpoint(HttpVerb.Get, "/guests/{id}")]
public partial class GetGuestAction : DomainAction<GuestDto, NotFoundError>
{
    private IGuestRepository _guests = null!;

    [FromRoute]
    public Guid Id { get; init; }

    public override async Task<Result<GuestDto, IError>> Execute(CancellationToken ct)
    {
        var guest = await _guests.GetByIdAsync(Id, ct);
        if (guest is null) return NotFoundError.For("Guest", Id);
        return GuestDto.FromEntity(guest);
    }
}
```

**Runtime result:** Works correctly, but the DomainAction invoker pipeline adds unnecessary overhead -- dependency resolution for `IDomainActionInvoker<T>`, pre/post action filters, activity tracing, and logging. For a simple read-by-ID, this is wasted work.

**Right:**

```csharp
[Endpoint(HttpVerb.Get, "/guests/{id}")]
public partial class GetGuestEndpoint : Endpoint<GuestDto, NotFoundError>
{
    private IGuestRepository _guests = null!;

    [FromRoute]
    public Guid Id { get; init; }

    public override async Task<Result<GuestDto, NotFoundError>> HandleAsync(CancellationToken ct)
    {
        var guest = await _guests.GetByIdAsync(Id, ct);
        if (guest is null) return NotFoundError.For("Guest", Id);
        return GuestDto.FromEntity(guest);
    }
}
```

**Why:** `DomainAction` is designed for operations that benefit from the invoker pipeline: validation, authorization filters, event dispatching, and cross-cutting concerns. Simple lookups that just call a repository and return a DTO should use `Endpoint<T>` directly. Reserve `DomainAction` for commands, mutations, and queries with business logic that needs pipeline support.

---

## 6. Not Registering UseRateLimiter() Middleware

**Wrong:**

```csharp
[Endpoint(HttpVerb.Post, "/auth/login")]
[RateLimit(Requests = 5, Window = "1m")]
public partial class LoginEndpoint : Endpoint<TokenResponse> { /* ... */ }

// Program.cs
builder.Services.AddPragmaticEndpoints();
var app = builder.Build();
// Missing: app.UseRateLimiter();
app.MapPragmaticEndpoints();
```

**Runtime result:** The `[RateLimit]` attribute causes the SG to generate `.RequireRateLimiting("__pragmatic_ratelimit_LoginEndpoint")` on the endpoint and register a fixed window policy in `AddPragmaticEndpoints()`. However, without `app.UseRateLimiter()` in the middleware pipeline, ASP.NET Core never enforces the policy. All requests pass through, even after exceeding the limit.

**Right:**

```csharp
// Program.cs
builder.Services.AddPragmaticEndpoints();
var app = builder.Build();
app.UseRateLimiter();  // Required for [RateLimit] enforcement
app.MapPragmaticEndpoints();
```

**Why:** ASP.NET Core's rate limiting is a middleware that must be explicitly added to the request pipeline. The SG generates the policy registration and endpoint metadata, but it cannot inject middleware into your pipeline. You must call `app.UseRateLimiter()` before `MapPragmaticEndpoints()`.

---

## 7. Expecting a Shared `[ResponseCache]` to Cache an Authenticated Read

**Wrong:**

```csharp
[Endpoint(HttpVerb.Get, "/orders/my")]
[ResponseCache(Duration = 300)]                       // Location.Any, the default
public partial class GetMyOrdersEndpoint : Endpoint<OrderDto[]>
{
    // reads the caller's orders
}
```

**Runtime result:** nothing is cached, for anybody who is signed in, and nothing says so.

The shared form generates an ASP.NET `CacheOutput` policy, and ASP.NET's default output-cache policy
refuses to cache a request whose user is authenticated. Adding `VaryByHeaders = ["Authorization"]` does
not change that: the refusal comes before the vary rules. Measured through the generated endpoint in
`Pragmatic.Integration.Tests/WhatASharedResponseCacheKeepsTests`: an anonymous request is answered from
the cache the second time, the same request made by a signed-in user runs the body every time, and a
second user never receives the first user's answer.

So the endpoint must be one anonymous callers reach: on a route that requires authentication the build
warns (`PRAG0554`). The output cache itself the generated host adds when a module declares a shared
`[ResponseCache]`; a host built by hand calls `app.UseOutputCache()`.

**Right:**

- A read whose answer is the same for everybody and reachable anonymously, a public catalogue, an
  availability search: keep `Location.Any` and `[AllowAnonymous]`
  (`services.UseOutputCacheFromPragmaticCaching()` to share the Pragmatic cache backend).
- A signed-in user's own data: let the browser keep it. The response carries `Cache-Control: private`
  and nothing shared ever holds it:

```csharp
[Endpoint(HttpVerb.Get, "/orders/my")]
[ResponseCache(Duration = 300, Location = ResponseCacheLocation.Client)]
public partial class GetMyOrdersEndpoint : Endpoint<OrderDto[]>
{
    // ...
}
```

- Server-side caching of a computed answer: `[Cacheable]` on the query or domain action
  (`Pragmatic.Caching`), whose key is partitioned by tenant and, where the answer depends on it, by
  caller.

---

## 8. Not Handling All Error Types in Result

**Wrong:**

```csharp
[Endpoint(HttpVerb.Post, "/orders")]
public partial class PlaceOrderEndpoint : Endpoint<OrderDto, NotFoundError, ValidationError>
{
    public override async Task<Result<OrderDto, NotFoundError, ValidationError>> HandleAsync(CancellationToken ct)
    {
        // Only returns NotFoundError, forgot about ValidationError
        var customer = await _customers.GetByIdAsync(CustomerId, ct);
        if (customer is null)
            return NotFoundError.For("Customer", CustomerId);

        // Validation logic that should return ValidationError but throws instead
        if (Items.Count == 0)
            throw new InvalidOperationException("Order must have at least one item");

        return new OrderDto(/* ... */);
    }
}
```

**Runtime result:** The `InvalidOperationException` bypasses the result pipeline entirely and produces a raw 500 Internal Server Error. The OpenAPI spec declares `ValidationError` as a possible 422 response, but it is never returned.

**Right:**

```csharp
[Endpoint(HttpVerb.Post, "/orders")]
public partial class PlaceOrderEndpoint : Endpoint<OrderDto, NotFoundError, ValidationError>
{
    public override async Task<Result<OrderDto, NotFoundError, ValidationError>> HandleAsync(CancellationToken ct)
    {
        if (Items.Count == 0)
            return ValidationError.For("Items", "validation.mincount", ("min", 1));

        var customer = await _customers.GetByIdAsync(CustomerId, ct);
        if (customer is null)
            return NotFoundError.For("Customer", CustomerId);

        return new OrderDto(/* ... */);
    }
}
```

**Why:** The SG auto-generates `builder.Produces<TError>(statusCode)` for every error type in the endpoint signature. Each error type has a default HTTP status mapping (e.g., `NotFoundError` = 404, `ValidationError` = 400). Return the typed error instead of throwing -- exceptions bypass the Result pipeline and always produce 500.

---

## 9. Applying File Validation Attributes to Non-File Properties

**Wrong:**

```csharp
[Endpoint(HttpVerb.Post, "/documents")]
public partial class UploadDocumentEndpoint : Endpoint<DocumentDto>
{
    [MaxFileSize(10 * 1024 * 1024)]
    [AllowedContentTypes("application/pdf")]
    public string DocumentUrl { get; set; } = "";  // string, not IFormFile!
}
```

**Runtime result:** The `[MaxFileSize]` and `[AllowedContentTypes]` attributes are silently ignored. No file size or content type validation occurs. The SG only generates validation checks for `IFormFile` or `IFormFileCollection` properties.

**Right:**

```csharp
[Endpoint(HttpVerb.Post, "/documents")]
public partial class UploadDocumentEndpoint : Endpoint<DocumentDto>
{
    [FromForm]
    [MaxFileSize(10 * 1024 * 1024)]
    [AllowedContentTypes("application/pdf")]
    public IFormFile Document { get; set; } = null!;
}
```

**Why:** `[MaxFileSize]` and `[AllowedContentTypes]` are HTTP-boundary validations specifically designed for file uploads. The SG generates checks that call `IFormFile.Length` and `IFormFile.ContentType` before `HandleAsync`. On a `string` property, there is no file to check -- the attributes have no effect and produce no warnings.

---

## 10. Throwing Exceptions Instead of Returning Error Types

**Wrong:**

```csharp
[Endpoint(HttpVerb.Get, "/invoices/{id}")]
public partial class GetInvoiceEndpoint : Endpoint<InvoiceDto, NotFoundError>
{
    private IInvoiceRepository _invoices = null!;

    [FromRoute]
    public Guid Id { get; init; }

    public override async Task<Result<InvoiceDto, NotFoundError>> HandleAsync(CancellationToken ct)
    {
        var invoice = await _invoices.GetByIdAsync(Id, ct);
        if (invoice is null)
            throw new KeyNotFoundException($"Invoice {Id} not found");

        return InvoiceDto.FromEntity(invoice);
    }
}
```

**Runtime result:** The exception propagates as an unhandled error, producing a 500 Internal Server Error with a stack trace (in Development) or a generic error page (in Production). The `NotFoundError` in the endpoint signature is never used. The OpenAPI spec declares a 404 response that is never returned.

**Right:**

```csharp
[Endpoint(HttpVerb.Get, "/invoices/{id}")]
public partial class GetInvoiceEndpoint : Endpoint<InvoiceDto, NotFoundError>
{
    private IInvoiceRepository _invoices = null!;

    [FromRoute]
    public Guid Id { get; init; }

    public override async Task<Result<InvoiceDto, NotFoundError>> HandleAsync(CancellationToken ct)
    {
        var invoice = await _invoices.GetByIdAsync(Id, ct);
        if (invoice is null)
            return NotFoundError.For("Invoice", Id);

        return InvoiceDto.FromEntity(invoice);
    }
}
```

**Why:** Pragmatic uses the Result pattern for all business logic outcomes. The generated handler maps each error type to an HTTP status code (`NotFoundError` = 404, `ValidationError` = 400, etc.) and serializes the error body. Exceptions bypass the pipeline entirely, lose type information, and always produce 500. Reserve exceptions for truly unexpected failures (e.g., database connection lost), not for expected business conditions.

---

## 11. Using [FromBody] Explicitly on Every Property

**Wrong:**

```csharp
[Endpoint(HttpVerb.Post, "/guests")]
public partial class CreateGuestEndpoint : Endpoint<GuestDto>
{
    [FromBody]
    public required string FirstName { get; init; }

    [FromBody]
    public required string LastName { get; init; }

    [FromBody]
    public required string Email { get; init; }

    [FromBody]
    public string? Phone { get; init; }
}
```

**Compile result:** Works, but the `[FromBody]` attributes are redundant. The SG already treats all properties that are not `[FromRoute]`, `[FromQuery]`, `[FromHeader]`, `[FromForm]`, or `[FromClaim]` as body properties, unless the operation carries a file, in which case there is no JSON body at all and they come from the form (entry 3).

**Right:**

```csharp
[Endpoint(HttpVerb.Post, "/guests")]
public partial class CreateGuestEndpoint : Endpoint<GuestDto>
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public string? Phone { get; init; }
}
```

Or, when mixing binding sources, use `[FromBody]` only on the ambiguous ones:

```csharp
[Endpoint(HttpVerb.Post, "/guests/{groupId}")]
public partial class CreateGuestEndpoint : Endpoint<GuestDto>
{
    [FromRoute]
    public Guid GroupId { get; init; }

    [FromHeader(Name = "X-Correlation-Id")]
    public string? CorrelationId { get; init; }

    // These are auto-detected as body -- no attribute needed
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
}
```

**Why:** The SG auto-infers body properties by exclusion: anything not bound to route, query, header, form, or claim is part of the request body. Adding `[FromBody]` everywhere adds noise without changing behavior. Note that `PRAG0512` (info-level diagnostic) fires when 5 or more properties implicitly bind to the body, nudging you to confirm the binding is intentional. In that case, adding `[FromBody]` to one property silences the diagnostic for clarity.

---

## Quick Reference

| Mistake | Diagnostic / Symptom |
|---------|---------------------|
| Missing `partial` | `PRAG0500` compile error |
| Route parameter unmatched | `PRAG0504` warning, parameter always default |
| Mixed body + form | Request fails or fields missing at runtime |
| Map before Add | `InvalidOperationException` or ignored options |
| DomainAction for simple reads | Works but unnecessary pipeline overhead |
| Missing `UseRateLimiter()` | Rate limits silently unenforced |
| Shared `[ResponseCache]` on an authenticated read | Nothing cached, silently |
| Unhandled error type | 500 instead of typed HTTP status |
| `typeof()` in attribute | No compile-time constraint checking |
| File attributes on non-file | Silently ignored, no validation |
| Throwing exceptions | 500 instead of Result-mapped status |
| Redundant `[FromBody]` | Harmless noise, consider removing |
