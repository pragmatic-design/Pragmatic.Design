---
title: "Pragmatic.Specification -- Getting Started"
description: "This guide covers defining specifications, composing them, using them with EF Core and in-memory collections, and writing unit tests."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Specification/docs/getting-started.md
sidebar:
  order: 2
---
This guide covers defining specifications, composing them, using them with EF Core and in-memory collections, and writing unit tests.

## Installation

```bash
dotnet add package Pragmatic.Specification
```

No additional setup is required. Import the namespaces:

```csharp
using Pragmatic.Specification;
```

---

## Your First Specification

A specification is a reusable predicate. Define it using `Spec<T>.Where()`:

```csharp
public static class ProductSpecs
{
    public static Specification<Product> InStock =>
        Spec<Product>.Where(p => p.StockQuantity > 0);

    public static Specification<Product> InCategory(string category) =>
        Spec<Product>.Where(p => p.Category == category);

    public static Specification<Product> PricedBelow(decimal maxPrice) =>
        Spec<Product>.Where(p => p.Price <= maxPrice);
}
```

Use it:

```csharp
// With EF Core
var products = await db.Products
    .Where(ProductSpecs.InStock)
    .ToListAsync();

// With in-memory list
var filtered = allProducts.Where(ProductSpecs.InStock).ToList();

// Test a single entity
bool available = ProductSpecs.InStock.IsSatisfiedBy(product);
```

---

## Composition

### And / Or / Not

```csharp
// AND: products in stock AND priced below 100
var spec = ProductSpecs.InStock.And(ProductSpecs.PricedBelow(100m));

// OR: products in "Electronics" OR "Books"
var spec = ProductSpecs.InCategory("Electronics")
    .Or(ProductSpecs.InCategory("Books"));

// NOT: products NOT in stock
var spec = ProductSpecs.InStock.Not();
```

### Operator Syntax

C# operators provide a more compact syntax:

```csharp
// & for AND
var spec = ProductSpecs.InStock & ProductSpecs.PricedBelow(100m);

// | for OR
var spec = ProductSpecs.InCategory("Electronics") | ProductSpecs.InCategory("Books");

// ! for NOT
var spec = !ProductSpecs.InStock;
```

### Conditional Composition

`AndIf` and `OrIf` only apply the second specification when the condition is true:

```csharp
public Specification<Product> BuildFilter(ProductFilterRequest request)
{
    var spec = Spec<Product>.True;  // Start with "match all"

    spec = spec.AndIf(request.InStockOnly, ProductSpecs.InStock);
    spec = spec.AndIf(request.MaxPrice.HasValue, ProductSpecs.PricedBelow(request.MaxPrice ?? 0));
    spec = spec.AndIf(!string.IsNullOrEmpty(request.Category),
        ProductSpecs.InCategory(request.Category!));

    return spec;
}
```

When no conditions are applied, `Spec<T>.True` passes through with no WHERE clause overhead.

---

## Using with EF Core

The `Where` extension method passes the specification's expression tree to EF Core, which translates it to SQL:

```csharp

// Single spec
var result = await db.Products
    .Where(ProductSpecs.InStock)
    .OrderBy(p => p.Name)
    .Take(10)
    .ToListAsync();

// Composed spec
var affordable = ProductSpecs.InStock & ProductSpecs.PricedBelow(50m);
var count = db.Products.Count(affordable);

// Check existence
bool hasElectronics = db.Products.Any(ProductSpecs.InCategory("Electronics"));
```

The expression tree is never compiled -- EF Core translates it directly to SQL.

---

## Using with In-Memory Collections

The same specification works with `IEnumerable<T>`. The expression is compiled to a delegate lazily on first use:

```csharp
var products = GetAllProducts();

var inStock = products.Where(ProductSpecs.InStock);
var first = products.FirstOrDefault(ProductSpecs.InCategory("Books"));
var allExpensive = products.All(ProductSpecs.PricedBelow(1000m));
```

---

## Extension Methods

All extensions work with both `IQueryable<T>` and `IEnumerable<T>`:

```csharp
// Filtering
collection.Where(spec)

// Existence checks
collection.Any(spec)
collection.All(spec)

// Counting
collection.Count(spec)

// Single element
collection.FirstOrDefault(spec)
collection.SingleOrDefault(spec)
```

---

## Unit Testing Specifications

Test specifications in isolation without a database using `IsSatisfiedBy`:

```csharp
public class ProductSpecsTests
{
    [Fact]
    public void InStock_WithPositiveQuantity_ReturnsTrue()
    {
        var product = new Product { StockQuantity = 5 };
        ProductSpecs.InStock.IsSatisfiedBy(product).Should().BeTrue();
    }

    [Fact]
    public void InStock_WithZeroQuantity_ReturnsFalse()
    {
        var product = new Product { StockQuantity = 0 };
        ProductSpecs.InStock.IsSatisfiedBy(product).Should().BeFalse();
    }

    [Fact]
    public void ComposedSpec_AndBothMatch_ReturnsTrue()
    {
        var product = new Product { StockQuantity = 10, Price = 25m };
        var spec = ProductSpecs.InStock & ProductSpecs.PricedBelow(50m);
        spec.IsSatisfiedBy(product).Should().BeTrue();
    }

    [Fact]
    public void ComposedSpec_OneDoesNotMatch_ReturnsFalse()
    {
        var product = new Product { StockQuantity = 10, Price = 75m };
        var spec = ProductSpecs.InStock & ProductSpecs.PricedBelow(50m);
        spec.IsSatisfiedBy(product).Should().BeFalse();
    }
}
```

---

## Custom Specification Classes

For complex specifications that need constructor parameters or additional logic, extend `Specification<T>`:

```csharp
public sealed class RecentOrdersSpec : Specification<Order>
{
    private readonly int _days;

    public RecentOrdersSpec(int days = 30)
    {
        _days = days;
    }

    public override Expression<Func<Order, bool>> ToExpression()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_days);
        return o => o.CreatedAt >= cutoff;
    }
}

// Usage
var recentOrders = new RecentOrdersSpec(7);
var orders = await db.Orders.Where(recentOrders).ToListAsync();
```

---

## Nested Property Access

Specifications support natural lambda access to nested properties:

```csharp
public static class CustomerSpecs
{
    public static Specification<Customer> InCity(string city) =>
        Spec<Customer>.Where(c => c.Address.City == city);

    public static Specification<Customer> HasRecentOrders =>
        Spec<Customer>.Where(c => c.Orders.Any(o => o.CreatedAt > DateTimeOffset.UtcNow.AddDays(-30)));
}
```

These translate to SQL JOINs and subqueries in EF Core.

---

## Spec<T>.True and Spec<T>.False

These are identity elements for composition:

| Identity | With And | With Or |
|----------|---------|---------|
| `True` | `True.And(x) == x` | `True.Or(x) == True` |
| `False` | `False.And(x) == False` | `False.Or(x) == x` |

Both are singletons (one instance per `T`), so there is no allocation overhead when using them as starting points for dynamic filter building.
