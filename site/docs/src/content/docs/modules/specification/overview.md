---
title: "Pragmatic.Specification"
description: "The specification pattern for composable, reusable query predicates in .NET 10."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Specification/README.md
sidebar:
  order: 0
  label: Overview
---
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
evaluation are settled. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

| [Concepts](/modules/specification/concepts/) | The pattern, expression vs delegate, when to use it |
| [Getting Started](/modules/specification/getting-started/) | Your first specification, querying, in-memory checks |
| [Composition Patterns](/modules/specification/composition-patterns/) | `And`/`Or`/`Not`, dynamic filters from user input, reusable building blocks |
| [Common Mistakes](/modules/specification/common-mistakes/) | The most frequent specification pitfalls |
| [Troubleshooting](/modules/specification/troubleshooting/) | Problem/solution guide |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](/modules/specification/overview/) ecosystem. See [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Specification is **MIT-licensed**.
