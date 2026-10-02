# Common Mistakes

These are the most common issues developers encounter when using Pragmatic.Specification. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Using Inline Lambdas Instead of Specifications

**Wrong:**

```csharp
public class OrderService(AppDbContext db)
{
    public async Task<List<Order>> GetActiveAsync(CancellationToken ct)
    {
        return await db.Orders
            .Where(o => !o.IsCancelled && !o.IsDeleted)
            .ToListAsync(ct);
    }

    public async Task<int> CountActiveAsync(CancellationToken ct)
    {
        return await db.Orders
            .Where(o => !o.IsCancelled && !o.IsDeleted)
            .CountAsync(ct);
    }
}
```

**Right:**

```csharp
public static class OrderSpecs
{
    public static Specification<Order> Active =>
        Spec<Order>.Where(o => !o.IsCancelled && !o.IsDeleted);
}

public class OrderService(AppDbContext db)
{
    public async Task<List<Order>> GetActiveAsync(CancellationToken ct)
    {
        return await db.Orders.Where(OrderSpecs.Active).ToListAsync(ct);
    }

    public async Task<int> CountActiveAsync(CancellationToken ct)
    {
        return await db.Orders.Count(OrderSpecs.Active);
    }
}
```

**Why:** Inline lambdas cannot be reused, tested in isolation, or composed with other predicates. When the "active" rule changes (e.g., adding `&& !o.IsArchived`), you update one specification instead of hunting through every repository and service method.

---

## 2. Calling Methods That EF Core Cannot Translate

**Wrong:**

```csharp
public static Specification<Order> RecentOrders =>
    Spec<Order>.Where(o => IsRecent(o.CreatedAt));

private static bool IsRecent(DateTimeOffset date)
{
    return date > DateTimeOffset.UtcNow.AddDays(-30);
}
```

**Runtime result:** `InvalidOperationException` -- "The LINQ expression could not be translated."

**Right:**

```csharp
public static Specification<Order> RecentOrders =>
    Spec<Order>.Where(o => o.CreatedAt > DateTimeOffset.UtcNow.AddDays(-30));
```

**Why:** EF Core translates expression trees to SQL. It can translate built-in operators and known method calls (like `DateTimeOffset.AddDays`), but it cannot translate arbitrary C# methods. Keep the expression body limited to operations that have SQL equivalents.

Common operations that **work** with EF Core:
- Comparison operators (`==`, `!=`, `<`, `>`, `<=`, `>=`)
- String methods (`Contains`, `StartsWith`, `EndsWith`, `ToLower`, `ToUpper`)
- `DateTimeOffset`/`DateTime` arithmetic (`AddDays`, `AddHours`)
- Collection methods (`Any`, `All`, `Count`)
- Null checks (`== null`, `!= null`)

Operations that **do not work**:
- Custom C# methods
- `ToString()` on non-string types
- Complex LINQ operations inside the predicate
- `Regex` matching

---

## 3. Building Dynamic Filters with if/else Instead of AndIf

**Wrong:**

```csharp
public IQueryable<Product> BuildQuery(ProductFilter filter, IQueryable<Product> query)
{
    if (filter.Category is not null)
        query = query.Where(p => p.Category == filter.Category);

    if (filter.MinPrice.HasValue)
        query = query.Where(p => p.Price >= filter.MinPrice.Value);

    if (filter.InStockOnly)
        query = query.Where(p => p.StockQuantity > 0);

    return query;
}
```

**Right:**

```csharp
public Specification<Product> BuildFilter(ProductFilter filter)
{
    var spec = Spec<Product>.True;

    spec = spec.AndIf(filter.Category is not null,
        ProductSpecs.InCategory(filter.Category!));

    spec = spec.AndIf(filter.MinPrice.HasValue,
        ProductSpecs.PricedAbove(filter.MinPrice!.Value));

    spec = spec.AndIf(filter.InStockOnly,
        ProductSpecs.InStock);

    return spec;
}
```

**Why:** The `if/else` approach works but loses the benefits of specifications: the resulting query is not testable in isolation, not reusable, and not composable with other specifications. With `AndIf`, you get a single `Specification<T>` that can be tested with `IsSatisfiedBy`, passed to repository methods, or further composed.

---

## 4. Newing Up Specifications Repeatedly Instead of Caching

**Wrong:**

```csharp
public async Task<List<Product>> GetActiveProductsAsync(CancellationToken ct)
{
    // New allocation on every call
    var spec = Spec<Product>.Where(p => p.IsActive && !p.IsDeleted);
    return await db.Products.Where(spec).ToListAsync(ct);
}
```

**Right:**

```csharp
public static class ProductSpecs
{
    // Allocated once, reused everywhere
    public static Specification<Product> Active { get; } =
        Spec<Product>.Where(p => p.IsActive && !p.IsDeleted);
}

public async Task<List<Product>> GetActiveProductsAsync(CancellationToken ct)
{
    return await db.Products.Where(ProductSpecs.Active).ToListAsync(ct);
}
```

**Why:** Parameterless specifications have no reason to be created on every call. Store them as static properties. This avoids unnecessary allocations and makes the predicate discoverable from a single location. Note: specifications with parameters (like `ForCustomer(Guid id)`) naturally create new instances per call -- that is expected.

---

## 5. Using `&&` / `||` Instead of `&` / `|` for Composition

**Wrong:**

```csharp
// Compiler error: cannot apply operator '&&' to Specification<T>
var spec = OrderSpecs.Active && OrderSpecs.HighValue(1000m);
```

**Right:**

```csharp
// Use single & and | (bitwise operators, overloaded by Specification<T>)
var spec = OrderSpecs.Active & OrderSpecs.HighValue(1000m);
var spec = OrderSpecs.Active | OrderSpecs.HighValue(1000m);

// Or use the method syntax
var spec = OrderSpecs.Active.And(OrderSpecs.HighValue(1000m));
```

**Why:** C# only allows overloading `&`, `|`, and `!` (bitwise operators), not `&&`, `||` (short-circuit logical operators). The `Specification<T>` class overloads the single-character operators. If you accidentally type `&&`, you get a compile error.

---

## 6. Forgetting the Extensions Namespace

**Wrong:**

```csharp
// Compiler error: IQueryable<T> does not contain a definition for 'Where'
// that accepts ISpecification<T>
var results = db.Products.Where(ProductSpecs.Active).ToListAsync();
```

**Right:**

```csharp

var results = db.Products.Where(ProductSpecs.Active).ToListAsync();
```

**Why:** The `Where`, `Any`, `All`, `Count`, `FirstOrDefault`, and `SingleOrDefault` overloads that accept `ISpecification<T>` are extension methods defined in `Pragmatic.Specification.SpecificationExtensions`. Without the `using` directive, the compiler cannot find them.

If you find yourself adding this using statement frequently, add it to a `GlobalUsings.cs` file:

```csharp
global using Pragmatic.Specification;
```

---

## 7. Assuming a Custom ISpecification<T> Composes by Its IsSatisfiedBy

`And`, `Or`, `AndIf`, and `OrIf` accept **any** `ISpecification<T>`, not just `Specification<T>`:

```csharp
ISpecification<Order> mySpec = GetSpecFromSomewhere();
var combined = OrderSpecs.Active.And(mySpec);   // ✅ compiles and works
```

A foreign `ISpecification<T>` (one that is not a `Specification<T>` subclass) is wrapped by reading
its **`ToExpression()`** — that expression is what gets merged into the combined tree.

**Wrong — a spec whose two members disagree:**

```csharp
public sealed class VipSpec : ISpecification<Order>
{
    // These two DO NOT describe the same rule.
    public Expression<Func<Order, bool>> ToExpression() => o => o.Total > 1000m;
    public bool IsSatisfiedBy(Order o) => o.Customer.IsVip;   // ← ignored once composed
}

var spec = OrderSpecs.Active.And(new VipSpec());
spec.IsSatisfiedBy(order);   // uses ToExpression() (Total > 1000), NOT IsVip
```

**Why:** the expression tree is the single source of truth. Once a spec is composed, the combined
`IsSatisfiedBy` compiles the merged **expression** — the foreign spec's own `IsSatisfiedBy` is never
called. Two consequences:

- Keep `IsSatisfiedBy` and `ToExpression()` describing the *same* rule. Subclassing
  `Specification<T>` (or using `Spec<T>.Where`) guarantees this for free — you only write the expression.
- A foreign spec that implements only `IsSatisfiedBy` and throws from `ToExpression()` will fail at the
  point of composition (`.And(...)`/`.Or(...)` reads the expression eagerly), not later. If you truly
  have an in-memory-only predicate, don't compose it — filter with `IEnumerable.Where(spec)` instead.

---

## 8. Putting Business Logic Inside ToExpression()

**Wrong:**

```csharp
public sealed class DiscountEligibleSpec : Specification<Customer>
{
    public override Expression<Func<Customer, bool>> ToExpression()
    {
        // Side effect inside expression!
        _logger.LogInformation("Checking discount eligibility");

        return c => c.OrderCount > 10 && c.TotalSpent > 5000m;
    }
}
```

**Right:**

```csharp
public sealed class DiscountEligibleSpec : Specification<Customer>
{
    public override Expression<Func<Customer, bool>> ToExpression()
        => c => c.OrderCount > 10 && c.TotalSpent > 5000m;
}
```

**Why:** `ToExpression()` returns an expression tree that may be called multiple times (for `And`/`Or` composition, for query translation, for compilation). It should be a pure function that returns a lambda expression with no side effects. Logging, validation, and other side effects belong in the service layer, not inside the specification.

---

## 9. Using Spec<T>.False as a Starting Point for AndIf

**Wrong:**

```csharp
public Specification<Order> BuildFilter(OrderFilterRequest request)
{
    var spec = Spec<Order>.False;  // Nothing matches!

    spec = spec.AndIf(request.ActiveOnly, OrderSpecs.Active);
    spec = spec.AndIf(request.MinTotal.HasValue, OrderSpecs.HighValue(request.MinTotal!.Value));

    return spec;  // Always returns no results
}
```

**Right:**

```csharp
public Specification<Order> BuildFilter(OrderFilterRequest request)
{
    var spec = Spec<Order>.True;  // Everything matches initially

    spec = spec.AndIf(request.ActiveOnly, OrderSpecs.Active);
    spec = spec.AndIf(request.MinTotal.HasValue, OrderSpecs.HighValue(request.MinTotal!.Value));

    return spec;
}
```

**Why:** `Spec<T>.False` never matches anything. `False.And(anything)` is still `False` -- it short-circuits. Use `Spec<T>.True` as the starting point for `AndIf` chains (it matches everything and progressively narrows). Use `Spec<T>.False` as the starting point for `OrIf` chains (it matches nothing and progressively widens).

| Starting Point | Composition | Use Case |
|----------------|-------------|----------|
| `Spec<T>.True` | `AndIf` | Narrow down from "all" |
| `Spec<T>.False` | `OrIf` | Widen from "none" |

---

## 10. Creating Specifications That Include Sorting or Projection

**Wrong:**

```csharp
public sealed class TopCustomersSpec : Specification<Customer>
{
    public override Expression<Func<Customer, bool>> ToExpression()
        => c => c.TotalSpent > 10000m;

    // These don't belong here
    public Expression<Func<Customer, decimal>> OrderBy => c => c.TotalSpent;
    public int Take => 10;
}
```

**Right:**

```csharp
// Specification: predicate only
public sealed class HighValueCustomerSpec : Specification<Customer>
{
    public override Expression<Func<Customer, bool>> ToExpression()
        => c => c.TotalSpent > 10000m;
}

// Sorting and paging are query concerns, not specification concerns
var topCustomers = await db.Customers
    .Where(new HighValueCustomerSpec())
    .OrderByDescending(c => c.TotalSpent)
    .Take(10)
    .ToListAsync(ct);
```

**Why:** Specifications define WHERE clauses -- nothing more. Sorting, paging, and projection are separate concerns that belong in the query layer. Mixing them into specifications breaks composability: you cannot combine a "top 10" specification with a "recent orders" specification in a meaningful way. Keep specifications focused on predicates and use [Pragmatic.Persistence](../../Pragmatic.Persistence/README.md) for the full query pipeline.
