---
title: "Architecture and Core Concepts"
description: "This guide explains **why** Pragmatic.Specification exists, how its pieces fit together, and how to think about composable predicates in a real codebase. Read t"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Specification/docs/concepts.md
sidebar:
  order: 1
---
This guide explains **why** Pragmatic.Specification exists, how its pieces fit together, and how to think about composable predicates in a real codebase. Read this before the individual feature guides.

---

## The Problem

Business rules live inside queries. Those queries are scattered across repositories, services, and controllers -- each with its own inline lambda. When the rules change, you update them in multiple places and hope you found them all.

### Scattered, duplicated filter logic

```csharp
// Repository
public async Task<List<Order>> GetActiveOrdersAsync(CancellationToken ct)
{
    return await _db.Orders
        .Where(o => !o.IsCancelled && !o.IsDeleted)   // rule #1
        .ToListAsync(ct);
}

// Service
public async Task<List<Order>> GetActiveHighValueOrdersAsync(decimal threshold, CancellationToken ct)
{
    return await _db.Orders
        .Where(o => !o.IsCancelled && !o.IsDeleted)   // rule #1 again
        .Where(o => o.Total >= threshold)
        .ToListAsync(ct);
}

// Controller
public async Task<int> CountActiveForCustomerAsync(Guid customerId, CancellationToken ct)
{
    return await _db.Orders
        .Where(o => !o.IsCancelled && !o.IsDeleted)   // rule #1 yet again
        .Where(o => o.CustomerId == customerId)
        .CountAsync(ct);
}
```

Three methods, three copies of "active order" logic. When you add a new status field -- say `IsArchived` -- you need to find and update every copy. Worse, the in-memory validation code often duplicates the same predicate using different syntax:

```csharp
// Validation helper
public bool IsOrderActive(Order order)
{
    return !order.IsCancelled && !order.IsDeleted;  // rule #1, fourth copy
}
```

Now you have four copies of the same rule, split between expression trees (for EF Core) and compiled delegates (for in-memory). They will drift apart.

### The consequences

| Problem | Impact |
|---------|--------|
| Duplicated predicates | One change requires updating N locations |
| Expression vs. delegate split | Database queries and in-memory checks use different code paths |
| No composability | Complex filters are built by chaining `.Where()` calls, which cannot be reused as building blocks |
| Untestable predicates | Inline lambdas inside repository methods cannot be unit-tested in isolation |
| Dynamic filters are painful | Building optional WHERE clauses from user input requires `if/else` chains that are hard to read and maintain |

---

## The Solution

Define each predicate **once** as a `Specification<T>`. Compose specifications with `And`, `Or`, and `Not`. Use the same specification for database queries (expression tree), in-memory filtering (compiled delegate), and unit tests (`IsSatisfiedBy`).

```csharp
// Define once
public static class OrderSpecs
{
    public static Specification<Order> Active =>
        Spec<Order>.Where(o => !o.IsCancelled && !o.IsDeleted);

    public static Specification<Order> HighValue(decimal threshold) =>
        Spec<Order>.Where(o => o.Total >= threshold);

    public static Specification<Order> ForCustomer(Guid customerId) =>
        Spec<Order>.Where(o => o.CustomerId == customerId);
}

// Use everywhere
var activeOrders = await db.Orders.Where(OrderSpecs.Active).ToListAsync(ct);
var highValue = await db.Orders.Where(OrderSpecs.Active & OrderSpecs.HighValue(1000m)).ToListAsync(ct);
var count = await db.Orders.Count(OrderSpecs.Active & OrderSpecs.ForCustomer(customerId));
bool isActive = OrderSpecs.Active.IsSatisfiedBy(order);
```

One definition. Four usages. Zero duplication.

**Where the class goes.** `OrderSpecs` is the shape for this library used on its own. With
Pragmatic.Persistence, every `[Entity]` already has a generated `static partial class
{Entity}Specifications` in the entity's namespace (it holds `ById`, and `By{LogicKey}`), so write the
rules as the other half of that partial, `public static partial class OrderSpecifications`, next to the
entity: one class per entity that every file already sees, instead of a second name and a `using` per
caller. The generator turns each static member into reads either way (`repo.FindActiveAsync()`,
`query.Active()`).

And every specification is independently testable:

```csharp
[Fact]
public void Active_ExcludesCancelledOrders()
{
    var order = new Order { IsCancelled = true };
    OrderSpecs.Active.IsSatisfiedBy(order).Should().BeFalse();
}
```

---

## How It Works

### Dual-path execution

Every specification carries an expression tree (`Expression<Func<T, bool>>`). This expression tree serves two purposes:

1. **IQueryable path** (database): The expression is handed directly to the query provider (EF Core, Dapper, etc.), which translates it to SQL. No compilation occurs.
2. **IEnumerable path** (in-memory): The expression is compiled to a `Func<T, bool>` delegate on first use, then cached via `Lazy<T>` for subsequent calls.

```
Specification<T>
    |
    +-- ToExpression()
    |       |
    |       +-- IQueryable<T>.Where(expr)  --> SQL WHERE clause
    |       |
    |       +-- Compiled to Func<T, bool>  --> In-memory filtering
    |
    +-- IsSatisfiedBy(entity)  --> Uses cached compiled delegate
```

This means you write the predicate once and the library chooses the optimal execution path based on context.

### Expression tree composition

When you combine two specifications with `And`, `Or`, or `Not`, the library creates a new expression tree that merges both. A `ParameterReplacer` unifies the lambda parameters so that the combined expression uses a single parameter -- this is required for EF Core to translate the combined expression to a single SQL query.

```csharp
// Two separate expressions:
// left:  o => !o.IsCancelled
// right: o => o.Total >= 1000

// After And composition:
// combined: x => !x.IsCancelled && x.Total >= 1000
//                 ^ unified parameter
```

Without parameter unification, EF Core would see two different parameter objects and fail to translate the expression.

### Lazy compilation and caching

The compiled delegate is created only when `IsSatisfiedBy` is first called:

```csharp
// Specification<T> base class
private readonly Lazy<Func<T, bool>> _compiled;

protected Specification()
{
    _compiled = new Lazy<Func<T, bool>>(() => ToExpression().Compile());
}

public bool IsSatisfiedBy(T entity)
{
    return _compiled.Value(entity);
}
```

If you only use specifications with `IQueryable<T>.Where(spec)`, no compilation ever occurs -- the expression tree is passed directly to the query provider. Compilation only happens on the first `IsSatisfiedBy` call, and the result is cached thread-safely.

---

## Core Types

### `ISpecification<T>` -- The Contract

The interface that all specifications implement:

```csharp
public interface ISpecification<T>
{
    Expression<Func<T, bool>> ToExpression();
    bool IsSatisfiedBy(T entity);
}
```

Two methods, one responsibility: define a predicate. `ToExpression()` returns the expression tree for query providers. `IsSatisfiedBy()` evaluates the predicate against a single entity.

### `Specification<T>` -- The Base Class

Abstract base class that adds composition. This is what you inherit from when creating custom specification classes:

```csharp
public abstract class Specification<T> : ISpecification<T>
{
    public abstract Expression<Func<T, bool>> ToExpression();
    public bool IsSatisfiedBy(T entity);

    // Composition -- accept any ISpecification<T>, not just Specification<T>
    public Specification<T> And(ISpecification<T> other);
    public Specification<T> Or(ISpecification<T> other);
    public Specification<T> Not();
    public Specification<T> AndIf(bool condition, ISpecification<T> other);
    public Specification<T> OrIf(bool condition, ISpecification<T> other);

    // Lazy-factory overloads -- the factory runs only when the condition is true
    public Specification<T> AndIf(bool condition, Func<Specification<T>> specFactory);
    public Specification<T> OrIf(bool condition, Func<Specification<T>> specFactory);

    // Operators
    public static Specification<T> operator &(left, right);  // And
    public static Specification<T> operator |(left, right);  // Or
    public static Specification<T> operator !(spec);          // Not
}
```

The composition methods create new specification instances -- they never mutate the original. This makes specifications safe to share across threads and to store as static properties.

### `Spec<T>` -- The Factory

Static factory for creating specifications without inheriting from `Specification<T>`:

```csharp
public static class Spec<T>
{
    public static Specification<T> Where(Expression<Func<T, bool>> predicate);
    public static Specification<T> True { get; }   // Always matches
    public static Specification<T> False { get; }   // Never matches
}
```

`Spec<T>.Where()` wraps a lambda in an `ExpressionSpecification<T>`. This is the most common way to create specifications for simple predicates.

`Spec<T>.True` and `Spec<T>.False` are singletons (one per type `T`). They serve as identity elements for composition:

| Identity | With And | With Or |
|----------|---------|---------|
| `True` | `True.And(x)` returns `x`'s behavior | `True.Or(x)` returns `True` |
| `False` | `False.And(x)` returns `False` | `False.Or(x)` returns `x`'s behavior |

`Spec<T>.True` is the standard starting point for building dynamic filters from user input.

---

## Composing Specifications

### And, Or, Not

```csharp
// AND: both must match
var spec = OrderSpecs.Active.And(OrderSpecs.HighValue(1000m));

// OR: either matches
var spec = OrderSpecs.ForCustomer(id1).Or(OrderSpecs.ForCustomer(id2));

// NOT: negate
var spec = OrderSpecs.Active.Not();
```

### Operator syntax

C# operators provide a more compact alternative:

```csharp
var spec = OrderSpecs.Active & OrderSpecs.HighValue(1000m);   // And
var spec = OrderSpecs.ForCustomer(id1) | OrderSpecs.ForCustomer(id2);  // Or
var spec = !OrderSpecs.Active;                                 // Not
```

### Complex compositions

Parentheses control precedence:

```csharp
// (overdue OR priority) AND NOT deleted
var urgent = (OrderSpecs.Overdue | OrderSpecs.Priority) & !OrderSpecs.Deleted;
```

This produces a single expression tree that EF Core translates to:

```sql
WHERE (IsOverdue = 1 OR Priority = 1) AND IsDeleted = 0
```

### Conditional composition

`AndIf` and `OrIf` apply the second specification only when the condition is `true`. When the condition is `false`, the original specification is returned unchanged -- no expression tree modification occurs.

```csharp
public Specification<Order> BuildFilter(OrderFilterRequest request)
{
    var spec = Spec<Order>.True;

    spec = spec.AndIf(request.CustomerId.HasValue,
        OrderSpecs.ForCustomer(request.CustomerId!.Value));

    spec = spec.AndIf(request.MinTotal.HasValue,
        OrderSpecs.HighValue(request.MinTotal!.Value));

    spec = spec.AndIf(request.ActiveOnly,
        OrderSpecs.Active);

    return spec;
}
```

When no conditions are applied, the result is `Spec<T>.True`, which matches everything. EF Core optimizes this to a query without a WHERE clause.

---

## Two Ways to Define Specifications

### Inline with `Spec<T>.Where()`

For simple predicates, use the factory:

```csharp
public static class ProductSpecs
{
    public static Specification<Product> InStock =>
        Spec<Product>.Where(p => p.StockQuantity > 0);

    public static Specification<Product> PricedBelow(decimal max) =>
        Spec<Product>.Where(p => p.Price <= max);
}
```

Best for: predicates that are a single lambda expression with zero or few parameters.

### Custom class extending `Specification<T>`

For predicates with constructor parameters, validation, or complex logic:

```csharp
public sealed class PriceRangeSpec(decimal min, decimal max) : Specification<Product>
{
    public override Expression<Func<Product, bool>> ToExpression()
        => product => product.Price >= min && product.Price <= max;
}
```

Best for: predicates with multiple parameters, predicates that need documentation, and predicates shared across bounded contexts.

### Organizing specifications

Group related specifications in a static class:

```csharp
public static class OrderSpecs
{
    // Parameterless -- static property
    public static Specification<Order> Active { get; } =
        Spec<Order>.Where(o => !o.IsCancelled && !o.IsDeleted);

    // With parameters -- static method
    public static Specification<Order> ForCustomer(Guid customerId) =>
        Spec<Order>.Where(o => o.CustomerId == customerId);

    // Complex -- dedicated class
    public static Specification<Order> InDateRange(DateTimeOffset from, DateTimeOffset to) =>
        new DateRangeSpec(from, to);
}
```

---

## Extension Methods

`SpecificationExtensions` provides LINQ-style methods that accept specifications. All are marked `[MethodImpl(MethodImplOptions.AggressiveInlining)]` for zero overhead.

### Available methods

| Method | IQueryable | IEnumerable |
|--------|-----------|-------------|
| `Where(spec)` | Expression tree to SQL | Compiled delegate |
| `Any(spec)` | Expression tree to SQL | Compiled delegate |
| `All(spec)` | Expression tree to SQL | Compiled delegate |
| `Count(spec)` | Expression tree to SQL | Compiled delegate |
| `FirstOrDefault(spec)` | Expression tree to SQL | Compiled delegate |
| `SingleOrDefault(spec)` | Expression tree to SQL | Compiled delegate |

### Usage

```csharp

// IQueryable (EF Core -- translates to SQL)
var orders = await db.Orders.Where(OrderSpecs.Active).ToListAsync(ct);
bool hasActive = await db.Orders.Any(OrderSpecs.Active);
int activeCount = await db.Orders.Count(OrderSpecs.Active);

// IEnumerable (in-memory)
var filtered = allOrders.Where(OrderSpecs.Active).ToList();
var first = allOrders.FirstOrDefault(OrderSpecs.HighValue(500m));
```

The extension methods choose the right path automatically:
- `IQueryable<T>` overloads call `spec.ToExpression()` and pass the expression to the query provider.
- `IEnumerable<T>` overloads call `spec.IsSatisfiedBy` as the predicate delegate.

---

## Integration with Pragmatic.Persistence

Specifications integrate directly with the [Pragmatic.Persistence](/modules/persistence/overview/) module's repository pattern.

### Repository queries

`IReadRepository<T>` accepts specifications for filtering:

```csharp
public class OrderQueryHandler(IReadRepository<Order> orders)
{
    public async Task<List<Order>> GetActiveForCustomer(Guid customerId, CancellationToken ct)
    {
        var spec = OrderSpecs.Active & OrderSpecs.ForCustomer(customerId);
        return await orders.FindAsync(spec, ct);
    }

    public async Task<bool> HasHighValueOrders(Guid customerId, CancellationToken ct)
    {
        var spec = OrderSpecs.ForCustomer(customerId) & OrderSpecs.HighValue(1000m);
        return await orders.ExistsAsync(spec, ct);
    }

    public async Task<int> CountActive(CancellationToken ct)
    {
        return await orders.CountAsync(OrderSpecs.Active, ct);
    }
}
```

### An operation's input, loaded by a rule

An action or a mutation can name a declared specification as the source of the rows it needs, instead
of injecting a repository: `[LoadEntity<Employee>(Specification = nameof(EmployeeSpecifications.ActiveWithNumber))]`
or `[LoadEntities<T>(Specification = …)]`. The specification's parameters bind by name to the operation's
properties, and the invoker reads the rows before the body runs: 404 for a single load that matches
nothing. Details in Pragmatic.Actions, *Troubleshooting* ("How do I load the rows a rule names").

### ComputedFilter

The `[ComputedFilter]` attribute in Pragmatic.Persistence generates specifications automatically from entity properties. These generated specifications compose with hand-written ones using the same `And`, `Or`, `Not` operators.

The other direction works too: a `[ComputedFilter]` or `[Projectable]` body can name a specification over a navigation (`Requests.Where(LeaveRequestSpecifications.Approved).Sum(r => r.Amount)`), and the generated expression hands the query the specification's `ToExpression()`, so the rule is written once. Its arguments cannot come from the row (PRAG0735). Details in Pragmatic.Persistence, *Projections and Query Views*.

---

## Nested Property Access

Specifications support lambda access to nested properties and collections. EF Core translates these to JOINs and subqueries:

```csharp
public static class CustomerSpecs
{
    // Nested property -- becomes JOIN in SQL
    public static Specification<Customer> InCity(string city) =>
        Spec<Customer>.Where(c => c.Address.City == city);

    // Collection navigation -- becomes subquery in SQL
    public static Specification<Customer> HasRecentOrders =>
        Spec<Customer>.Where(c =>
            c.Orders.Any(o => o.CreatedAt > DateTimeOffset.UtcNow.AddDays(-30)));

    // String operations -- supported by all providers
    public static Specification<Customer> NameContains(string term) =>
        Spec<Customer>.Where(c => c.Name.Contains(term));
}
```

---

## Unit Testing

Specifications are independently testable without a database, using `IsSatisfiedBy`:

```csharp
public class OrderSpecsTests
{
    [Fact]
    public void Active_WithCancelledOrder_ReturnsFalse()
    {
        var order = new Order { IsCancelled = true };
        OrderSpecs.Active.IsSatisfiedBy(order).Should().BeFalse();
    }

    [Fact]
    public void Active_WithValidOrder_ReturnsTrue()
    {
        var order = new Order { IsCancelled = false, IsDeleted = false };
        OrderSpecs.Active.IsSatisfiedBy(order).Should().BeTrue();
    }

    [Fact]
    public void ComposedSpec_BothMatch_ReturnsTrue()
    {
        var order = new Order { IsCancelled = false, IsDeleted = false, Total = 1500m };
        var spec = OrderSpecs.Active & OrderSpecs.HighValue(1000m);
        spec.IsSatisfiedBy(order).Should().BeTrue();
    }

    [Fact]
    public void ComposedSpec_OneFails_ReturnsFalse()
    {
        var order = new Order { IsCancelled = false, IsDeleted = false, Total = 500m };
        var spec = OrderSpecs.Active & OrderSpecs.HighValue(1000m);
        spec.IsSatisfiedBy(order).Should().BeFalse();
    }
}
```

No mocking, no database setup, no HTTP requests. Create an entity, pass it to the specification, assert the result.

---

## Internal Specification Types

The library provides six internal implementations of `Specification<T>`. You never instantiate these directly -- they are created by `Spec<T>.Where()`, `And()`, `Or()`, and `Not()`.

| Type | Created By | Expression |
|------|-----------|------------|
| `ExpressionSpecification<T>` | `Spec<T>.Where(lambda)` | Wraps the user-provided lambda |
| `AndSpecification<T>` | `.And()` or `&` | `Expression.AndAlso(left, right)` |
| `OrSpecification<T>` | `.Or()` or `\|` | `Expression.OrElse(left, right)` |
| `NotSpecification<T>` | `.Not()` or `!` | `Expression.Not(inner)` |
| `TrueSpecification<T>` | `Spec<T>.True` | `_ => true` (singleton) |
| `FalseSpecification<T>` | `Spec<T>.False` | `_ => false` (singleton) |

All composition types use `ParameterReplacer` to unify lambda parameters, ensuring the resulting expression tree is valid for query translation.

---

## Design Principles

1. **Specifications are predicates only.** They define WHERE clauses. Sorting, paging, projection, and includes belong to the query system ([Pragmatic.Persistence](/modules/persistence/overview/)).

2. **Dual-path execution.** The same specification works with `IQueryable` (expression tree for SQL) and `IEnumerable` (compiled delegate for in-memory). You never write separate implementations.

3. **Immutability.** Composition creates new instances. The originals are never modified. This makes specifications safe for static fields and concurrent access.

4. **Lazy compilation.** The expression is not compiled until `IsSatisfiedBy` is called. If the specification is only used with `IQueryable`, no compilation ever occurs.

5. **Thread-safe caching.** The compiled delegate is cached via `Lazy<T>`, which provides thread-safe initialization by default.

6. **Zero dependencies.** The library depends only on `System.Linq.Expressions` from the base class library. No NuGet packages, no source generator.

---

## See Also

| Topic | Location |
|-------|----------|
| Getting Started | [getting-started.md](/modules/specification/getting-started/) |
| Composition Patterns | [composition-patterns.md](/modules/specification/composition-patterns/) |
| Common Mistakes | [common-mistakes.md](/modules/specification/common-mistakes/) |
| Troubleshooting | [troubleshooting.md](/modules/specification/troubleshooting/) |
| Pragmatic.Persistence | [Pragmatic.Persistence](/modules/persistence/overview/) |
