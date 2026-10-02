---
title: "DomainAction Integration"
description: "Guide to using Pragmatic.Actions with Pragmatic.Endpoints for CQRS patterns."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Endpoints/docs/domain-action-integration.md
sidebar:
  order: 4
---
Guide to using Pragmatic.Actions with Pragmatic.Endpoints for CQRS patterns.

## Overview

Pragmatic.Endpoints integrates seamlessly with Pragmatic.Actions, allowing you to expose DomainActions as HTTP endpoints with full pipeline support (validation, filters, observability).

## Endpoint Types Comparison

| Pattern | Base Class | DI Pattern | Pipeline | Use Case |
|---------|------------|------------|----------|----------|
| Raw Endpoint | `Endpoint<T>` | `SetDependencies` | None | Simple CRUD, direct logic |
| DomainAction | `DomainAction<T>` | `DomainActionInvoker<T>` | Full | CQRS, complex business logic |

## Creating a DomainAction Endpoint

### 1. Define the Action with Endpoint Attribute

```csharp
using Pragmatic.Actions;
using Pragmatic.Endpoints;
using Pragmatic.Result;

[Endpoint(HttpVerb.Post, "/orders")]
[ApiSummary("Place Order")]
[ApiTags("Orders")]
[HttpStatus(201)]
public partial class PlaceOrder : DomainAction<OrderId, ValidationError, ConflictError, NotFoundError>
{
    // Dependencies - injected by DomainActionInvoker
    private IOrderRepository Orders { get; set; } = null!;
    private ICustomerRepository Customers { get; set; } = null!;

    // Route parameters
    // (none in this example, but would use [FromRoute])

    // Header parameters
    [FromHeader(Name = "X-Idempotency-Key")]
    public required string IdempotencyKey { get; init; }

    // Body properties (no attribute = body)
    public required Guid CustomerId { get; init; }
    public required List<OrderItem> Items { get; init; }
    public string? Notes { get; init; }

    public override async Task<Result<OrderId, IError>> Execute(CancellationToken ct)
    {
        // Check idempotency
        if (await Orders.ExistsAsync(IdempotencyKey, ct))
            return ConflictError.AlreadyExists("Order", IdempotencyKey);

        // Validate customer
        var customer = await Customers.GetByIdAsync(CustomerId, ct);
        if (customer is null)
            return NotFoundError.For("Customer", CustomerId);

        // Create order
        var order = new Order(customer, Items, Notes);
        await Orders.CreateAsync(order, ct);

        return order.Id;
    }
}
```

### 2. Generated Handler

The generator creates a handler using `DomainActionInvoker<T>`:

```csharp
// Generated: PlaceOrder.Handler.g.cs
internal static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder endpoints)
{
    var builder = endpoints.MapPost("/orders", async (
        [FromHeader(Name = "X-Idempotency-Key")] string idempotencyKey,
        [FromBody] PlaceOrderBody body,
        IDomainActionInvoker<PlaceOrder, OrderId> invoker,
        HttpContext httpContext,
        CancellationToken ct
    ) =>
    {
        var action = new PlaceOrder
        {
            IdempotencyKey = idempotencyKey,
            CustomerId = body.CustomerId,
            Items = body.Items,
            Notes = body.Notes
        };

        var result = await invoker.InvokeAsync(action, ct);

        return result.Match(
            (orderId) => Results.Created($"/orders/{orderId}", orderId),
            (ValidationError error) => MapError(error),
            (ConflictError error) => MapError(error),
            (NotFoundError error) => MapError(error)
        );
    });

    // OpenAPI metadata
    builder.WithSummary("Place Order");
    builder.WithTags("Orders");
    builder.Produces<OrderId>(201);
    builder.ProducesProblem(422);  // ValidationError
    builder.ProducesProblem(409);  // ConflictError
    builder.ProducesProblem(404);  // NotFoundError

    return builder;
}
```

## Void DomainActions

For operations without a return value:

```csharp
[Endpoint(HttpVerb.Delete, "/users/{id}")]
[ApiTags("Users")]
public partial class DeleteUser : VoidDomainAction<NotFoundError, ForbiddenError>
{
    private IUserRepository Users { get; set; } = null!;

    [FromRoute]
    public Guid Id { get; init; }

    [FromQuery]
    public bool? SoftDelete { get; init; }

    public override async Task<VoidResult<IError>> Execute(CancellationToken ct)
    {
        var user = await Users.GetByIdAsync(Id, ct);
        if (user is null)
            return NotFoundError.For("User", Id);

        if (!user.CanBeDeleted)
            return ForbiddenError.ActionDenied("delete", "User");

        if (SoftDelete == true)
            await Users.SoftDeleteAsync(Id, ct);
        else
            await Users.DeleteAsync(Id, ct);

        return Success;
    }
}
```

Generated handler uses `IVoidDomainActionInvoker<T>`:

```csharp
var result = await invoker.InvokeAsync(action, ct);

return result.Match(
    () => Results.NoContent(),
    (error) => MapError(error)
);
```

## Pipeline Integration

DomainAction endpoints get full pipeline support:

### Validation

```csharp
[Endpoint(HttpVerb.Post, "/users")]
public partial class CreateUser : DomainAction<UserId, ValidationError>
{
    [Required]
    [Email]
    public required string Email { get; init; }

    [Required]
    [MinLength(2)]
    public required string Name { get; init; }

    // Validation runs BEFORE Execute via ValidationFilter
    public override async Task<Result<UserId, IError>> Execute(CancellationToken ct)
    {
        // If we get here, validation passed
        var user = new User(Email, Name);
        await Users.CreateAsync(user, ct);
        return user.Id;
    }
}
```

### Filters

All registered `IActionFilter` instances apply:

```csharp
// Registration
services.AddActionFilter<LoggingFilter>();
services.AddActionFilter<AuditFilter>();
services.AddActionFilter<TransactionFilter>();

// Filter execution order:
// 1. BeforeExecute (ValidationFilter → AuditFilter → TransactionFilter)
// 2. Execute
// 3. AfterExecute (TransactionFilter → AuditFilter → ValidationFilter)
```

### Observability

Automatic tracing and logging:

```
Activity: "Action.PlaceOrder"
Tags:
  action.type: "PlaceOrder"
  action.result: "Success" | "Failure"
  action.error: "ValidationError" (if failed)
```

## Boundary Interfaces

DomainAction endpoints are included in boundary interfaces:

```csharp
// Define boundary
namespace MyApp.Orders;

[Boundary]
public partial class OrdersBoundary { }

// Actions in namespace
[Endpoint(HttpVerb.Post, "/orders")]
public partial class PlaceOrder : DomainAction<OrderId> { }

[Endpoint(HttpVerb.Delete, "/orders/{id}")]
public partial class CancelOrder : VoidDomainAction<NotFoundError> { }

// Generated interface
public partial interface IOrdersActions
{
    Task<Result<OrderId>> PlaceOrder(...);
    Task<VoidResult<NotFoundError>> CancelOrder(Guid id, ...);
}
```

## Registration

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register DomainAction infrastructure
builder.Services.AddPragmaticActions();

// Register endpoints (includes invokers for DomainAction endpoints)
builder.Services.AddPragmaticEndpoints();

// Register action-specific services
builder.Services.AddOrdersActions();

var app = builder.Build();

// Map all endpoints
app.MapPragmaticEndpoints();
```

## Mixed Endpoints

Combine raw endpoints and DomainAction endpoints:

```csharp
// Simple query - raw endpoint
[Endpoint(HttpVerb.Get, "/orders/{id}")]
public partial class GetOrderEndpoint : Endpoint<OrderDto>
{
    private IOrderRepository _orders = null!;

    [FromRoute]
    public Guid Id { get; set; }

    public override async Task<Result<OrderDto>> HandleAsync(CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(Id, ct);
        return order is not null
            ? new OrderDto(order)
            : NotFoundError.For("Order", Id);
    }
}

// Complex command - DomainAction
[Endpoint(HttpVerb.Post, "/orders")]
public partial class PlaceOrder : DomainAction<OrderId, ValidationError>
{
    // Full pipeline support for complex business logic
}
```

## Internal Actions

Use `[DomainAction(Internal = true)]` for actions not exposed as endpoints:

```csharp
// Public endpoint
[Endpoint(HttpVerb.Post, "/orders")]
public partial class PlaceOrder : DomainAction<OrderId>
{
    public override async Task<Result<OrderId, IError>> Execute(CancellationToken ct)
    {
        // Create order
        var orderId = await CreateOrder();

        // Call internal action (not an HTTP endpoint)
        await Invoker.InvokeAsync(new RecalculateInventory
        {
            ProductIds = Items.Select(i => i.ProductId).ToList()
        }, ct);

        return orderId;
    }
}

// Internal action - NOT exposed as endpoint
[DomainAction(Internal = true)]
public partial class RecalculateInventory : VoidDomainAction
{
    public required List<Guid> ProductIds { get; init; }
}
```

## Best Practices

1. **Use DomainActions for complex logic** - Validation, multiple dependencies, side effects
2. **Use raw endpoints for simple queries** - Direct database reads
3. **Always use typed errors** - In DomainAction generics
4. **Include idempotency** - For POST/PUT operations
5. **Leverage validation** - Use attributes, let pipeline handle it
6. **Keep actions focused** - Single responsibility
