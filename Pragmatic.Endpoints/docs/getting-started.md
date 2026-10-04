# Getting Started with Pragmatic.Endpoints

This guide will get you up and running with Pragmatic.Endpoints in 5 minutes.

## Installation

```bash
dotnet add package Pragmatic.Endpoints
```

## Quick Start

### 1. Create Your First Endpoint

```csharp
using Pragmatic.Endpoints;
using Pragmatic.Result;

[Endpoint(HttpVerb.Get, "/hello")]
public partial class HelloEndpoint : Endpoint<HelloResponse>
{
    [FromQuery]
    public string? Name { get; set; }

    public override Task<Result<HelloResponse>> HandleAsync(CancellationToken ct)
    {
        var greeting = $"Hello, {Name ?? "World"}!";
        return Task.FromResult<Result<HelloResponse>>(new HelloResponse(greeting));
    }
}

public record HelloResponse(string Message);
```

### 2. Register in Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register generated services
builder.Services.AddPragmaticEndpoints();

var app = builder.Build();

// Map all endpoints
app.MapPragmaticEndpoints();

app.Run();
```

### 3. Test It

```bash
curl http://localhost:5000/hello?name=Developer
# {"message":"Hello, Developer!"}
```

## Key Concepts

### Endpoint Types

| Base Class | Use Case |
|------------|----------|
| `Endpoint<T>` | Returns a response |
| `VoidEndpoint` | No response body (204 No Content) |
| `DomainAction<T>` | CQRS pattern with DI invoker |
| `VoidDomainAction` | CQRS void operation |

### Binding Attributes

| Attribute | Source | Example |
|-----------|--------|---------|
| `[FromRoute]` | URL path | `/users/{id}` → `Guid Id` |
| `[FromQuery]` | Query string | `?page=1` → `int Page` |
| `[FromHeader]` | HTTP headers | `X-Api-Key` → `string ApiKey` |
| None | Request body | Auto-detected public properties |

### Error Handling

```csharp
[Endpoint(HttpVerb.Get, "/users/{id}")]
public partial class GetUserEndpoint : Endpoint<UserDto>
{
    private IUserRepository _users = null!;

    [FromRoute]
    public Guid Id { get; set; }

    public override async Task<Result<UserDto>> HandleAsync(CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(Id, ct);
        if (user is null)
            return NotFoundError.For("User", Id);  // Returns 404

        return new UserDto(user);  // Returns 200
    }
}
```

## Adding Dependencies

Private fields are auto-detected as dependencies:

```csharp
[Endpoint(HttpVerb.Get, "/products")]
public partial class ListProductsEndpoint : Endpoint<List<ProductDto>>
{
    // These become constructor parameters in generated handler
    private IProductRepository _products = null!;
    private ILogger<ListProductsEndpoint> _logger = null!;

    public override async Task<Result<List<ProductDto>>> HandleAsync(CancellationToken ct)
    {
        _logger.LogInformation("Listing products");
        var products = await _products.GetAllAsync(ct);
        return products.Select(p => new ProductDto(p)).ToList();
    }
}
```

The generator creates a `SetDependencies` method:

```csharp
// Generated
internal void SetDependencies(
    IProductRepository products,
    ILogger<ListProductsEndpoint> logger)
{
    _products = products;
    _logger = logger;
}
```

## OpenAPI Documentation

```csharp
[Endpoint(HttpVerb.Post, "/orders")]
[ApiSummary("Create Order")]
[ApiDescription("Creates a new order for the authenticated customer.")]
[ApiTags("Orders")]
[HttpStatus(201)]
public partial class CreateOrderEndpoint : Endpoint<OrderResponse>
{
    /// <summary>
    /// The customer ID.
    /// </summary>
    public required Guid CustomerId { get; set; }

    /// <summary>
    /// Items to order.
    /// </summary>
    public required List<OrderItem> Items { get; set; }
}
```

This generates:

- **Summary**: "Create Order"
- **Description**: "Creates a new order..."
- **Tags**: ["Orders"]
- **Success Response**: 201 Created with `OrderResponse`
- **Request Body**: Generated DTO with property documentation

### Two documents, and which one to publish

- **The compile-time document**, at `/openapi/v1.json`. It is written by the generator from the
  manifest, so it carries what only the generator knows: every declared error with its status,
  permissions, examples, idempotency headers. It describes the generated endpoints and nothing else.
  A Composition host that references `Pragmatic.Endpoints.OpenApi` publishes it **in Development**
  without being asked (with Scalar over it at `/scalar` when `Scalar.AspNetCore` is referenced), and
  in every environment after `app.UseApiDocumentation()`; Scalar stays in Development. Outside a
  Composition host, `app.MapPragmaticOpenApi()` maps it; a host that maps it itself keeps its route.
- **ASP.NET's runtime document**: `services.AddOpenApi()` and `app.MapOpenApi()`, the one Scalar,
  Swagger UI and client generators plug into. It describes the generated endpoints **and** the ones the
  application maps by hand, side by side. Add `services.AddPragmaticOpenApi()` to enrich its
  operations with the manifest's errors and permissions.

The enrichment reads the manifests registered when the application loads. Each assembly that declares
endpoints registers its own, and a Composition host registers every module's at once as well; a module
seen both ways is read once. The assembly with the endpoints needs nothing beyond `Pragmatic.Endpoints`
for this; only the project that calls `AddPragmaticOpenApi()` references `Pragmatic.Endpoints.OpenApi`.

A generated endpoint is mapped as a `RequestDelegate`, so that it survives an AOT publish, and ASP.NET's
API explorer skips a handler it cannot see. The generated registration adds a description provider
that reads what the generator attached to the endpoint instead: the bound parameters with their place
and type, the body's content types, the responses. Nothing to call.

`PragmaticEndpointsOptions.EnableOpenApi = false` takes the generated operations out of both documents,
and leaves the application's own endpoints where they are.

⚠️ ASP.NET's runtime document cannot hold a schema that nests more than 64 levels once built. The
usual cause is an operation that answers with an **entity**: its navigations reach that depth quickly
(on the Showcase, `Reservation` nests 79 and `Guest` 103). Such a response is described there as JSON
without a schema, and a warning names the endpoint and the type; without that, the whole document
would answer 500. The compile-time document describes it in full. Answering with a DTO keeps the schema
in both.

## DomainAction Integration

For CQRS patterns with the Actions module:

```csharp
[Endpoint(HttpVerb.Delete, "/users/{id}")]
[ApiTags("Users")]
public partial class DeleteUser : VoidDomainAction<NotFoundError, ForbiddenError>
{
    private IUserRepository Users { get; set; } = null!;

    [FromRoute]
    public Guid Id { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct)
    {
        var user = await Users.GetByIdAsync(Id, ct);
        if (user is null)
            return NotFoundError.For("User", Id);

        if (!user.CanBeDeleted)
            return ForbiddenError.ActionDenied("delete", "User");

        await Users.DeleteAsync(Id, ct);
        return Success;
    }
}
```

The generator uses `DomainActionInvoker<T>` for full pipeline support (validation, filters, etc.).

## Next Steps

- [Binding Reference](binding-reference.md) - All binding scenarios
- [DomainAction Integration](domain-action-integration.md) - CQRS patterns
- [Response Caching](response-caching.md) - Output cache and distributed caching
- [Error Handling](error-handling.md) - HTTP status mapping
