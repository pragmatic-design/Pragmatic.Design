---
title: "Composition Patterns"
description: "Specifications shine when you compose simple predicates into complex queries. Instead of duplicating filter logic across repositories and services, you define e"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Specification/docs/composition-patterns.md
sidebar:
  order: 3
---
Specifications shine when you compose simple predicates into complex queries. Instead of duplicating filter logic across repositories and services, you define each predicate once and combine them with `&`, `|`, and `!` operators.

---

## Basic Composition

### AND: Both conditions must match

```csharp
var activeProducts = new IsActiveSpec() & new InCategorySpec("Electronics");

// Equivalent to:
var activeProducts = new IsActiveSpec().And(new InCategorySpec("Electronics"));
```

### OR: Either condition matches

```csharp
var urgentOrders = new IsOverdueSpec() | new IsPrioritySpec();
```

### NOT: Negate a condition

```csharp
var excludeDeleted = !new IsDeletedSpec();
```

### Complex Composition

```csharp
var targetOrders = (new IsOverdueSpec() | new IsPrioritySpec()) & !new IsDeletedSpec();

// SQL equivalent:
// WHERE (IsOverdue = 1 OR Priority = 1) AND IsDeleted = 0
```

---

## Conditional Composition

When filters are optional (e.g., search parameters that may or may not be provided), use `AndIf` and `OrIf` to avoid null-checking:

```csharp
public Specification<Product> BuildFilter(ProductSearchRequest request)
{
    var spec = Spec<Product>.True;

    spec = spec.AndIf(request.CategoryId.HasValue,
        new InCategorySpec(request.CategoryId!.Value));

    spec = spec.AndIf(!string.IsNullOrEmpty(request.SearchTerm),
        new NameContainsSpec(request.SearchTerm!));

    spec = spec.AndIf(request.MinPrice.HasValue,
        Spec<Product>.Where(p => p.Price >= request.MinPrice!.Value));

    spec = spec.AndIf(request.ActiveOnly,
        new IsActiveSpec());

    return spec;
}
```

`AndIf(false, ...)` returns the original specification unchanged: no expression tree modification. This is more efficient than building `if/else` chains.

---

## Defining Reusable Specifications

Each specification is a class with a single responsibility:

```csharp
public sealed class IsActiveSpec : Specification<Product>
{
    public override Expression<Func<Product, bool>> ToExpression()
        => product => product.IsActive && !product.IsDeleted;
}

public sealed class InCategorySpec(Guid categoryId) : Specification<Product>
{
    public override Expression<Func<Product, bool>> ToExpression()
        => product => product.CategoryId == categoryId;
}

public sealed class PriceRangeSpec(decimal min, decimal max) : Specification<Product>
{
    public override Expression<Func<Product, bool>> ToExpression()
        => product => product.Price >= min && product.Price <= max;
}

public sealed class NameContainsSpec(string term) : Specification<Product>
{
    public override Expression<Func<Product, bool>> ToExpression()
        => product => product.Name.Contains(term);
}
```

---

## Using with Repositories

Specifications integrate directly with `IReadRepository<T>`:

```csharp
public class ProductQueryHandler(IReadRepository<Product> products)
{
    public async Task<List<Product>> GetActiveInCategory(Guid categoryId, CancellationToken ct)
    {
        var spec = new IsActiveSpec() & new InCategorySpec(categoryId);
        return await products.FindAsync(spec, ct);
    }

    public async Task<bool> HasExpensiveProducts(Guid categoryId, CancellationToken ct)
    {
        var spec = new InCategorySpec(categoryId) & new PriceRangeSpec(1000, decimal.MaxValue);
        return await products.ExistsAsync(spec, ct);
    }

    public async Task<int> CountActive(CancellationToken ct)
    {
        return await products.CountAsync(new IsActiveSpec(), ct);
    }
}
```

---

## Using with IQueryable

Specifications work directly on `IQueryable<T>` via extension methods:

```csharp
public class ProductService(AppDbContext db)
{
    public async Task<List<Product>> SearchAsync(ProductSearchRequest request, CancellationToken ct)
    {
        var spec = BuildFilter(request);

        return await db.Products
            .Where(spec)
            .OrderBy(p => p.Name)
            .Take(request.PageSize)
            .ToListAsync(ct);
    }
}
```

The `Where(spec)` extension calls `spec.ToExpression()` and passes the expression tree to EF Core, which translates it to SQL.

---

## Using with In-Memory Collections

The same specifications work for in-memory filtering via `IsSatisfiedBy()`:

```csharp
var activeProducts = allProducts.Where(new IsActiveSpec());

// Or check a single item
if (new IsActiveSpec().IsSatisfiedBy(product))
    Console.WriteLine("Product is active");
```

The expression is compiled to a `Func<T, bool>` on first call and cached (thread-safe via `Lazy<T>`).

---

## Static Specifications

For specifications with no parameters, expose them as static properties:

```csharp
public static class ProductSpecs
{
    public static Specification<Product> Active { get; } = new IsActiveSpec();
    public static Specification<Product> Deleted { get; } = Spec<Product>.Where(p => p.IsDeleted);
    public static Specification<Product> Expensive { get; } = Spec<Product>.Where(p => p.Price > 1000);

    public static Specification<Product> InCategory(Guid id) => new InCategorySpec(id);
    public static Specification<Product> PriceRange(decimal min, decimal max) => new PriceRangeSpec(min, max);
}

// Usage
var results = await products.FindAsync(ProductSpecs.Active & ProductSpecs.InCategory(categoryId), ct);
```

---

## Spec<T> Factory

For inline specifications without a dedicated class, use `Spec<T>.Where()`:

```csharp
var recentOrders = Spec<Order>.Where(o => o.CreatedAt > DateTimeOffset.UtcNow.AddDays(-7));
var highValue = Spec<Order>.Where(o => o.Total > 500);

var targetOrders = recentOrders & highValue;
```

### Special Instances

```csharp
Spec<Product>.True   // Matches everything; useful as starting point for conditional composition
Spec<Product>.False  // Matches nothing; useful for access-denied scenarios
```

---

## Expression Extension Methods

Beyond `Where()`, specifications provide additional query extensions:

| Extension | Description |
|-----------|-------------|
| `queryable.Where(spec)` | Filter by specification |
| `queryable.Any(spec)` | Check if any match |
| `queryable.All(spec)` | Check if all match |
| `queryable.Count(spec)` | Count matches |
| `queryable.FirstOrDefault(spec)` | First match or null |
| `queryable.SingleOrDefault(spec)` | Single match or null |

All extensions work on both `IQueryable<T>` (SQL) and `IEnumerable<T>` (in-memory).

---

## Design Rules

1. **Predicates only**: specifications define WHERE clauses. Sorting, paging, and projection belong elsewhere.
2. **Dual-path execution**: every specification works both on `IQueryable` (translated to SQL) and `IEnumerable` (compiled to delegate).
3. **Immutable**: composition creates new instances; the original specifications are not modified.
4. **Expression-safe**: only use expressions that EF Core can translate to SQL. Avoid calling C# methods that have no SQL equivalent.
