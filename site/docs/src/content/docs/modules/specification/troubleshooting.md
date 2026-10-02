---
title: "Troubleshooting"
description: "Practical problem/solution guide for Pragmatic.Specification. Each section covers a common issue, the likely causes, and the fix."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Specification/docs/troubleshooting.md
sidebar:
  order: 5
---
Practical problem/solution guide for Pragmatic.Specification. Each section covers a common issue, the likely causes, and the fix.

---

## Specification Not Filtering (All Results Returned)

Your specification compiles and runs, but the query returns all rows instead of the filtered subset.

### Checklist

1. **Did you import the extensions namespace?** Without `using Pragmatic.Specification;`, the standard LINQ `Where` is used instead of the specification-aware overload. The compiler may not warn you because `Where(spec)` could be interpreted as `Where(Func<T, bool>)` via implicit conversion in some edge cases.

2. **Are you using `Spec<T>.True` without further composition?** `Spec<T>.True` matches everything. If your `AndIf` conditions are all `false`, the spec remains `True`.

3. **Is the specification applied to the right `DbSet`?** Double-check that you are querying the correct entity type.

4. **Is the predicate logic correct?** Test with `IsSatisfiedBy` against a known entity:

   ```csharp
   var order = new Order { IsCancelled = true };
   bool result = OrderSpecs.Active.IsSatisfiedBy(order);
   // If result is true, the predicate logic is wrong
   ```

---

## EF Core Cannot Translate the Expression

You get `InvalidOperationException: The LINQ expression 'xxx' could not be translated` at runtime.

### Possible Causes

**Custom method call in the expression.** EF Core can only translate known .NET methods to SQL. If your specification calls a custom method, EF Core cannot translate it:

```csharp
// This will fail at runtime with EF Core
Spec<Order>.Where(o => CalculateAge(o.CreatedAt) > 30)
```

**Fix:** Inline the logic directly in the lambda, using only EF Core-translatable operations:

```csharp
Spec<Order>.Where(o => o.CreatedAt < DateTimeOffset.UtcNow.AddDays(-30))
```

**Unsupported string operations.** Not all `string` methods are supported by all providers. For example, `string.Format` is generally not translatable.

**Provider-specific limitations.** Some expressions translate on PostgreSQL but not SQL Server, or vice versa. Check your provider's documentation for supported translations.

### Debugging Strategy

1. Extract the expression with `spec.ToExpression()` and inspect it.
2. Try the lambda directly in a `.Where()` call to confirm EF Core can translate it.
3. If the expression is a composition, test each part individually to find which one fails.

---

## Composed Specification Produces Wrong SQL

The AND/OR composition generates unexpected results in SQL.

### Possible Causes

**Operator precedence.** Without parentheses, `&` binds tighter than `|`:

```csharp
// This means: A OR (B AND C) -- not (A OR B) AND C
var spec = specA | specB & specC;
```

**Fix:** Use explicit parentheses:

```csharp
var spec = (specA | specB) & specC;
```

**Stale cached delegate.** If you modified a specification class but the old compiled delegate is cached (e.g., in a static field), you may see stale results in tests. This is rare in production but can happen in test suites that reuse static instances.

---

## IsSatisfiedBy Returns Unexpected Results

The specification works correctly with EF Core but returns wrong results when testing with `IsSatisfiedBy`.

### Possible Causes

**Date/time evaluation differences.** Expressions like `DateTimeOffset.UtcNow` are captured at different times. In EF Core, the value is translated to `GETUTCDATE()` (evaluated at query time). In `IsSatisfiedBy`, it is evaluated at delegate compilation time and cached.

```csharp
// ToExpression captures "now" at compilation time for in-memory evaluation
public sealed class RecentOrdersSpec : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        return o => o.CreatedAt >= cutoff;
    }
}
```

**Fix for time-sensitive tests:** Create a new specification instance for each test to get a fresh `UtcNow` value. Or use `Pragmatic.Temporal.IClock` for deterministic time in tests.

**Entity state differences.** The entity you pass to `IsSatisfiedBy` may have different property values than what exists in the database. Verify the entity state matches your expectations.

---

## Performance: Specification Creates Too Many Allocations

You are composing many specifications in a hot loop and seeing allocation pressure.

### Recommendations

1. **Cache parameterless specifications as static properties:**

   ```csharp
   // One allocation, reused forever
   public static Specification<Order> Active { get; } =
       Spec<Order>.Where(o => !o.IsCancelled && !o.IsDeleted);
   ```

2. **Pre-compose common combinations:**

   ```csharp
   public static Specification<Order> ActiveHighValue { get; } =
       Active & HighValue(1000m);
   ```

3. **Avoid composing specifications inside tight loops.** Build the specification once before the loop, not inside it.

4. **For truly hot paths, consider using the expression directly** instead of creating specification objects. Specifications are designed for business logic clarity, not nanosecond-level performance.

---

## Extension Method Ambiguity

The compiler reports an ambiguous call between `System.Linq.Queryable.Where` and `SpecificationExtensions.Where`.

### Fix

This happens when the specification type is `ISpecification<T>` instead of `Specification<T>`. The compiler sees `ISpecification<T>` as a potential `Func<T, bool>` candidate.

Ensure your variable is typed as `Specification<T>`:

```csharp
// May cause ambiguity
ISpecification<Order> spec = OrderSpecs.Active;
db.Orders.Where(spec);  // Ambiguous

// No ambiguity
Specification<Order> spec = OrderSpecs.Active;
db.Orders.Where(spec);  // Resolves to SpecificationExtensions.Where
```

---

## FAQ

### Can I use specifications with Dapper or raw SQL?

Not directly. Specifications produce expression trees, which are designed for LINQ query providers (EF Core, LINQ to DB). For Dapper, extract the predicate logic into a shared method and build both the specification and the SQL WHERE clause from it.

### Can I compose specifications across different entity types?

No. `Specification<T>` is generic -- `Specification<Order>` cannot compose with `Specification<Customer>`. Each specification type is bound to a single entity. For cross-entity filtering, compose at the query level using navigation properties.

### Is there a performance penalty for composed specifications vs. inline lambdas?

For `IQueryable` (EF Core): no. The composed expression tree produces the same SQL as a hand-written lambda with `&&` and `||`. For `IEnumerable`: the compiled delegate has one extra level of function calls, which is negligible. The expression tree composition itself (creating `AndSpecification`, `OrSpecification`) allocates a few objects, but this is a one-time cost per composition, not per evaluation.

### Can I serialize specifications?

No. Specifications contain expression trees, which are not serializable. If you need to send filter criteria across a network boundary, use a DTO (like `OrderFilterRequest`) and reconstruct the specification on the receiving side.

### Do specifications work with async methods?

`IsSatisfiedBy` is synchronous because it evaluates a compiled delegate in memory. For async queries, use the extension methods with `IQueryable`:

```csharp
var results = await db.Orders.Where(spec).ToListAsync(ct);
```

The extension methods chain with standard LINQ operators, which support async via EF Core's `ToListAsync`, `CountAsync`, etc.

---

## Getting Help

| Resource | Location |
|----------|----------|
| Concepts and architecture | [concepts.md](/modules/specification/concepts/) |
| Composition patterns | [composition-patterns.md](/modules/specification/composition-patterns/) |
| Common mistakes | [common-mistakes.md](/modules/specification/common-mistakes/) |
| Module README | [../README.md](/modules/specification/overview/) |
| Pragmatic.Persistence integration | [Pragmatic.Persistence](/modules/persistence/overview/) |
