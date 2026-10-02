# Pragmatic.Specification

The specification pattern for composable, reusable query predicates in .NET 10.

## The Problem

Business rules embedded in queries get duplicated across repositories, services, and validators, each
copy an inline lambda. Change the rule and you hunt every method; worse, SQL (expression trees) and
in-memory checks (compiled delegates) drift apart.

```csharp
db.Orders.Where(o => !o.IsCancelled && !o.IsDeleted)                 // repository
db.Orders.Where(o => !o.IsCancelled && !o.IsDeleted && o.Total >= t) // service: same rule again
!order.IsCancelled && !order.IsDeleted                               // validator: same rule, drifts
```

## The Solution

Encapsulate a rule once as a `Specification<T>`, compose with `And`/`Or`/`Not`, and use the **same**
specification for EF Core queries (translated to SQL) and in-memory checks (`IsSatisfiedBy`).

```csharp
public sealed class ActiveOrder : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression()
        => o => !o.IsCancelled && !o.IsDeleted;
}

db.Orders.Where(new ActiveOrder().And(new OrderOverThreshold(100m)));   // → SQL
bool ok = new ActiveOrder().IsSatisfiedBy(order);                      // in-memory, same rule
```

One definition, reusable and independently unit-testable; dynamic filters compose without `if`-chains.

## Installation

```bash
dotnet add package Pragmatic.Specification
```

## Status

**Stable** within 1.0.0-alpha: composition (`And`/`Or`/`Not`), EF Core translation, and in-memory
evaluation are settled. See the [roadmap](../docs/ROADMAP.md).

## Documentation

| Guide | What you'll learn |
|-------|-------------------|
| [Concepts](docs/concepts.md) | The pattern, expression vs delegate, when to use it |
| [Getting Started](docs/getting-started.md) | Your first specification, querying, in-memory checks |
| [Composition Patterns](docs/composition-patterns.md) | `And`/`Or`/`Not`, dynamic filters from user input, reusable building blocks |
| [Common Mistakes](docs/common-mistakes.md) | The most frequent specification pitfalls |
| [Troubleshooting](docs/troubleshooting.md) | Problem/solution guide |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](../README.md) ecosystem. See [Licensing](../docs/LICENSING.md).
Pragmatic.Specification is **MIT-licensed**.
