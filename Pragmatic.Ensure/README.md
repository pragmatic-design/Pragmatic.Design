# Pragmatic.Ensure

Guard clauses for parameter validation in .NET: consistent, fast, and intention-revealing.

## The Problem

Every codebase validates inputs, and without a shared approach guard clauses become a patchwork:
inconsistent exception types, forgotten guards, and exceptions thrown for business cases that should
return typed errors.

```csharp
_repository = repository ?? throw new ArgumentNullException(nameof(repository));   // dev A
_logger = logger;                                                                  // dev B forgot
if (id == Guid.Empty) throw new InvalidOperationException("Id cannot be empty");   // dev C, wrong type
if (order is null) throw new KeyNotFoundException($"Order {id} not found");        // dev D, business case
```

## The Solution

One consistent, readable guard API that throws the right exception with the right message, and a clear
boundary: guards are for *programmer errors* (preconditions), not business outcomes (use
[Result](../Pragmatic.Result/README.md) for those).

```csharp
using static Pragmatic.Ensure.Ensure;

public OrderService(IOrderRepository repository, ILogger<OrderService> logger)
{
    ThrowIfNull(repository);
    ThrowIfNull(logger);
    // ...
}

public async Task<Order> GetOrderAsync(Guid id)
{
    ThrowIfEmpty(id);                          // ArgumentException with the right message
    return await _repository.GetByIdAsync(id);
}
```

Argument names are captured automatically via `[CallerArgumentExpression]`, no `nameof` noise. The
guards are aggressively inlined and allocation-free on the success path.

## Installation

```bash
dotnet add package Pragmatic.Ensure
```

## Status

**Stable** within 1.0.0-alpha: the guard surface is settled. See the [roadmap](../docs/ROADMAP.md).

## Documentation

| Guide | What you'll learn |
|-------|-------------------|
| [Concepts](docs/concepts.md) | Guards vs Result, the precondition boundary, design |
| [Getting Started](docs/getting-started.md) | Your first guards |
| [API Reference](docs/api-reference.md) | Every `ThrowIf*` / `Is*` / `Check*`, parameters, null-safety contract |
| [Best Practices](docs/best-practices.md) | When to guard vs return a typed error |
| [Common Mistakes](docs/common-mistakes.md) | The most frequent guard pitfalls |
| [Troubleshooting](docs/troubleshooting.md) | Problem/solution guide |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](../README.md) ecosystem. See [Licensing](../docs/LICENSING.md).
Pragmatic.Ensure is **MIT-licensed**.
