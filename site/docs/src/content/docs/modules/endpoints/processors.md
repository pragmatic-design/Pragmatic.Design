---
title: "Endpoint Processors"
description: "Endpoint processors are hooks that run before or after the handler, enabling cross-cutting concerns like validation, authorization checks, audit logging, and no"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Endpoints/docs/processors.md
sidebar:
  order: 10
---
Endpoint processors are hooks that run before or after the handler, enabling cross-cutting concerns like validation, authorization checks, audit logging, and notifications without polluting the handler logic itself. They compose via attributes and execute in a deterministic pipeline.

---

## The Problem

Endpoints often need logic that runs before the handler (pre-conditions, input enrichment, resource existence checks) or after it (audit logs, metrics, notifications). Without processors, this logic ends up:

1. **Duplicated** across handlers — every endpoint that checks "does the customer exist?" repeats the same code.
2. **Tangled** with business logic — the handler does too many things, violating SRP.
3. **Hard to reorder** — changing the execution order means refactoring the handler.

Processors solve this by externalizing pre/post logic into reusable, ordered, DI-resolved classes.

---

## Pre-Processors

A pre-processor runs **before** the endpoint handler. It can inspect the request, modify context, and short-circuit the pipeline by returning an error.

### Interface

```csharp
public interface IEndpointPreProcessor
{
    ValueTask<PreProcessorResult> ProcessAsync(IEndpointContext context, CancellationToken ct = default);
}
```

### Typed Variant

When you need access to the endpoint instance (e.g., to read its properties after binding):

```csharp
public interface IEndpointPreProcessor<TEndpoint> : IEndpointPreProcessor
    where TEndpoint : class
{
    ValueTask<PreProcessorResult> ProcessAsync(
        TEndpoint endpoint, IEndpointContext context, CancellationToken ct = default);
}
```

The untyped `ProcessAsync` has a default implementation that casts `context.Endpoint` and delegates to the typed method.

### PreProcessorResult

A readonly struct that determines whether the pipeline continues:

```csharp
public readonly struct PreProcessorResult
{
    public bool ShouldContinue { get; }
    public IError? Error { get; }

    public static PreProcessorResult Continue();
    public static PreProcessorResult Fail(IError error);
    public static PreProcessorResult NotFound(string resourceType, string? id = null);
    public static PreProcessorResult NotFound<TEntity>(string? id = null);
}
```

- `Continue()` — proceed to the next processor or handler.
- `Fail(error)` — stop the pipeline and return the error as HTTP response.
- `NotFound(...)` — shorthand for `Fail(NotFoundError.Create(...))`. Returns HTTP 404.

### Example: Validate Customer Exists

```csharp
public class ValidateCustomerProcessor(ICustomerRepository customers) : IEndpointPreProcessor
{
    public async ValueTask<PreProcessorResult> ProcessAsync(IEndpointContext context, CancellationToken ct)
    {
        var customerId = context.GetRouteValue<Guid>("customerId");
        if (customerId == default)
            return PreProcessorResult.Continue(); // Not relevant for this endpoint

        var exists = await customers.ExistsAsync(
            new CustomerByIdSpec(customerId.Value), ct);

        return exists
            ? PreProcessorResult.Continue()
            : PreProcessorResult.NotFound<Customer>(customerId.ToString());
    }
}
```

### Example: Typed Pre-Processor

```csharp
public class CheckInventoryProcessor : IEndpointPreProcessor<PlaceOrderEndpoint>
{
    private readonly IInventoryService _inventory;

    public CheckInventoryProcessor(IInventoryService inventory) => _inventory = inventory;

    public async ValueTask<PreProcessorResult> ProcessAsync(
        PlaceOrderEndpoint endpoint, IEndpointContext context, CancellationToken ct)
    {
        // Access endpoint properties directly (after binding)
        var available = await _inventory.CheckAvailabilityAsync(endpoint.ProductId, endpoint.Quantity, ct);

        return available
            ? PreProcessorResult.Continue()
            : PreProcessorResult.Fail(new InsufficientStockError(endpoint.ProductId));
    }
}
```

---

## Post-Processors

A post-processor runs **after** the handler, regardless of whether it succeeded or failed. It receives the handler result for inspection.

### Interface

```csharp
public interface IEndpointPostProcessor
{
    ValueTask ProcessAsync(IEndpointContext context, object? result, CancellationToken ct = default);
}
```

### Typed Variant

```csharp
public interface IEndpointPostProcessor<TEndpoint> : IEndpointPostProcessor
    where TEndpoint : class
{
    ValueTask ProcessAsync(
        TEndpoint endpoint, IEndpointContext context, object? result, CancellationToken ct = default);
}
```

### Example: Audit Logging

```csharp
public class AuditLogProcessor(IAuditService audit) : IEndpointPostProcessor
{
    public async ValueTask ProcessAsync(IEndpointContext context, object? result, CancellationToken ct)
    {
        await audit.LogAsync(new AuditEntry
        {
            Endpoint = context.EndpointName,
            User = context.User?.Identity?.Name,
            Timestamp = DateTimeOffset.UtcNow,
            Success = result is not IError
        }, ct);
    }
}
```

### Example: Notification After Order

```csharp
public class OrderNotificationProcessor : IEndpointPostProcessor<PlaceOrderEndpoint>
{
    private readonly INotificationService _notifications;

    public OrderNotificationProcessor(INotificationService notifications) => _notifications = notifications;

    public async ValueTask ProcessAsync(
        PlaceOrderEndpoint endpoint, IEndpointContext context, object? result, CancellationToken ct)
    {
        if (result is OrderDto order)
        {
            await _notifications.SendAsync(
                $"Order {order.Id} placed for customer {endpoint.CustomerId}", ct);
        }
    }
}
```

---

## Attaching Processors

Use `[PreProcessor<T>]` and `[PostProcessor<T>]` attributes on endpoint classes:

```csharp
[Endpoint(HttpVerb.Post, "/api/orders")]
[PreProcessor<ValidateCustomerProcessor>(Order = 0)]
[PreProcessor<CheckInventoryProcessor>(Order = 1)]
[PostProcessor<AuditLogProcessor>]
[PostProcessor<OrderNotificationProcessor>]
public partial class PlaceOrderEndpoint : Endpoint<OrderDto>
{
    public required Guid CustomerId { get; init; }
    public required Guid ProductId { get; init; }
    public required int Quantity { get; init; }

    public override async Task<Result<OrderDto>> HandleAsync(CancellationToken ct)
    {
        // Only business logic here — pre-conditions handled by processors
        var order = await PlaceOrder(CustomerId, ProductId, Quantity, ct);
        return order;
    }
}
```

---

## Execution Pipeline

The complete endpoint execution pipeline with processors:

```
Request arrives
  |
  v
Model binding (route, query, body, headers, claims)
  |
  v
[PreProcessor #1] (Order = 0) → Continue? → [PreProcessor #2] (Order = 1) → Continue?
  |                                                |
  | (Fail)                                         | (Fail)
  v                                                v
Return error response                     Return error response
  |
  v (all Continue)
HandleAsync() — the endpoint handler
  |
  v
[PostProcessor #1] — always runs
  |
  v
[PostProcessor #2] — always runs
  |
  v
Response serialization (Result → HTTP)
```

### Ordering Rules

1. **Declaration order** is the default: processors run in the order they appear as attributes.
2. **`Order` property** overrides declaration order: lower values run first.
3. **Pre-processors short-circuit**: if one returns `Fail`, subsequent pre-processors and the handler are skipped.
4. **Post-processors always run**: they execute even if the handler returned an error.
5. **DI resolution**: processors are resolved from the DI container per request (scoped lifetime).

---

## DI Registration

Nothing to write: naming a processor in `[PreProcessor<T>]` / `[PostProcessor<T>]` registers it. The
generated handler resolves it from the request services, and the same generator emits
`TryAddScoped<TProcessor>` — in the assembly's own `AddPragmaticEndpoints()`, and in the Composition
host's `RegisterAllEndpoints()`, which builds its own registration from the assembly metadata.

Scoped, because a processor may inject anything the request can reach — a repository, a `DbContext`,
the current user. `TryAdd`, so a registration you write yourself keeps precedence: a different
lifetime, or a factory supplying an argument the container cannot resolve on its own.

```csharp
// Registered by the attribute — no startup code.
public class AuditLogProcessor(IAuditService audit) : IEndpointPostProcessor
{
    // ...
}

// Only when you need something other than the default.
builder.Services.AddSingleton<StatelessHeaderProcessor>();
```

A processor the container cannot build — abstract, or without a public constructor — gets no
registration, and `PRAG0534` says so at compile time instead of letting the first request fail.

---

## Common Patterns

### Resource Existence Check

Pre-processor that verifies a resource exists before the handler runs:

```csharp
public class EnsureProductExistsProcessor(IProductRepository products) : IEndpointPreProcessor
{
    public async ValueTask<PreProcessorResult> ProcessAsync(IEndpointContext context, CancellationToken ct)
    {
        var productId = context.GetRouteValue<Guid>("productId");
        if (productId is null) return PreProcessorResult.Continue();

        return await products.ExistsAsync(new ProductByIdSpec(productId.Value), ct)
            ? PreProcessorResult.Continue()
            : PreProcessorResult.NotFound<Product>(productId.ToString());
    }
}
```

### Metrics Collection

Post-processor that records endpoint latency:

```csharp
public class MetricsProcessor(IMeterFactory meterFactory) : IEndpointPostProcessor
{
    private static readonly Histogram<double> Duration =
        meterFactory.Create("app").CreateHistogram<double>("endpoint.duration.ms");

    public ValueTask ProcessAsync(IEndpointContext context, object? result, CancellationToken ct)
    {
        // Note: actual timing would need a Stopwatch started in a pre-processor
        Duration.Record(1.0, new("endpoint", context.EndpointName));
        return ValueTask.CompletedTask;
    }
}
```

### Conditional Processing

Pre-processor that only applies to certain endpoints:

```csharp
public class TenantIsolationProcessor(ITenantContext tenant) : IEndpointPreProcessor
{
    public ValueTask<PreProcessorResult> ProcessAsync(IEndpointContext context, CancellationToken ct)
    {
        if (!tenant.IsResolved)
            return ValueTask.FromResult(
                PreProcessorResult.Fail(UnauthorizedError.Create("Tenant context required")));

        return ValueTask.FromResult(PreProcessorResult.Continue());
    }
}
```

---

## API Reference

### Attributes

| Attribute | Target | Properties |
|-----------|--------|------------|
| `[PreProcessor<T>]` | Class | `Order` (int, default 0) |
| `[PostProcessor<T>]` | Class | `Order` (int, default 0) |

### Interfaces

| Interface | Method | Returns |
|-----------|--------|---------|
| `IEndpointPreProcessor` | `ProcessAsync(context, ct)` | `ValueTask<PreProcessorResult>` |
| `IEndpointPreProcessor<T>` | `ProcessAsync(endpoint, context, ct)` | `ValueTask<PreProcessorResult>` |
| `IEndpointPostProcessor` | `ProcessAsync(context, result, ct)` | `ValueTask` |
| `IEndpointPostProcessor<T>` | `ProcessAsync(endpoint, context, result, ct)` | `ValueTask` |

### PreProcessorResult

| Factory | HTTP Result | When to Use |
|---------|-------------|-------------|
| `Continue()` | — (pipeline continues) | Validation passed |
| `Fail(IError)` | Error's StatusCode | Business rule violation |
| `NotFound(type, id?)` | 404 | Resource does not exist |
| `NotFound<T>(id?)` | 404 | Type-safe resource not found |
