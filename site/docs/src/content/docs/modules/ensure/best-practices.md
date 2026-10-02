---
title: "Best Practices"
description: "Guidelines for using Pragmatic.Ensure effectively."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Ensure/docs/best-practices.md
sidebar:
  order: 3
---
Guidelines for using Pragmatic.Ensure effectively.

## Choosing the Right Pattern

| Scenario | Pattern | Package |
|----------|---------|---------|
| Constructor guards, API preconditions | `ThrowIf*` | Pragmatic.Ensure |
| Quick boolean checks in conditionals | `Is*` | Pragmatic.Ensure |
| Domain/user input validation with typed errors | `Check.*` | Pragmatic.Ensure.Result |

## Rule 1: Guards Are for Programming Errors

Use `ThrowIf*` methods for programming errors—situations that should never occur if the code is correct.

```csharp
// Good: Guards catch bugs early
public void ProcessOrder(Order order, Customer customer)
{
    Ensure.ThrowIfNull(order);
    Ensure.ThrowIfNull(customer);

    // If we reach here, order and customer are definitely not null
}
```

Don't use guards for expected business conditions:

```csharp
// Bad: User not found is a business case, not a bug
public User GetUser(int id)
{
    var user = _db.Users.Find(id);
    Ensure.ThrowIfNull(user);  // Don't do this!
    return user;
}

// Good: Use Result pattern for business cases
public Result<User, NotFoundError> GetUser(int id)
{
    var user = _db.Users.Find(id);
    if (user is null)
        return NotFoundError.For("User", id);
    return user;
}
```

## Rule 2: Guard at the Boundary

Place guards at public API boundaries—constructors and public methods:

```csharp
public class OrderService
{
    public OrderService(IOrderRepository repository, ILogger<OrderService> logger)
    {
        // Guard constructor parameters
        Ensure.ThrowIfNull(repository);
        Ensure.ThrowIfNull(logger);

        _repository = repository;
        _logger = logger;
    }

    public async Task<Order> CreateOrder(CreateOrderRequest request)
    {
        // Guard public method parameters
        Ensure.ThrowIfNull(request);
        Ensure.ThrowIfNullOrEmpty(request.Items);

        return await CreateOrderInternal(request);
    }

    // Private methods don't need guards—caller already validated
    private async Task<Order> CreateOrderInternal(CreateOrderRequest request)
    {
        // ...
    }
}
```

## Rule 3: Is* for Flow Control

Use `Is*` methods when the validation result affects program flow:

```csharp
public void ImportProducts(IEnumerable<ProductDto> products)
{
    foreach (var product in products)
    {
        // Skip invalid products, don't crash
        if (!Ensure.IsNotNullOrWhiteSpace(product.Name))
        {
            _logger.LogWarning("Skipping product with empty name");
            continue;
        }

        if (!Ensure.IsPositive(product.Price))
        {
            _logger.LogWarning("Skipping product with invalid price");
            continue;
        }

        ImportProduct(product);
    }
}
```

## Rule 3.5: Check.* for Domain Validation

Use `Check.*` methods when validating user input or business rules where failure is an **expected outcome** and you need typed errors:

```csharp
using Pragmatic.Ensure.Result;

public Result<User, ValidationError> CreateUser(CreateUserDto dto)
{
    // Validation failures are expected - use Check pattern
    var validation = Check.NotNullOrWhiteSpace(dto.Name, ValidationError.For("Name", "validation.required"))
        .Then(() => Check.Email(dto.Email, ValidationError.For("Email", "validation.email")))
        .Then(() => Check.InRange(dto.Age, 18, 120, ValidationError.For("Age", "validation.range", ("min", 18), ("max", 120))));

    if (validation.IsFailure)
        return validation.Error;

    return new User(dto.Name, dto.Email, dto.Age);
}
```

### When Check.* > ThrowIf*

| Use Check.* | Use ThrowIf* |
|-------------|--------------|
| User-provided input | Caller-provided dependencies |
| Business rule validation | API contract enforcement |
| Expected invalid data | Unexpected invalid data (bugs) |
| Need typed error details | Need fast-fail with stack trace |

## Rule 4: Don't Over-Guard

Guard what matters, not everything:

```csharp
// Bad: Over-guarding
public decimal CalculateTotal(decimal subtotal, decimal taxRate, decimal discount)
{
    Ensure.ThrowIfNegative(subtotal);
    Ensure.ThrowIfNegative(taxRate);
    Ensure.ThrowIfNegative(discount);
    Ensure.ThrowIfGreaterThan(taxRate, 1m);
    Ensure.ThrowIfGreaterThan(discount, subtotal);
    // ... 10 more guards

    return (subtotal * (1 + taxRate)) - discount;
}

// Good: Guard the essential invariants
public decimal CalculateTotal(decimal subtotal, decimal taxRate, decimal discount)
{
    Ensure.ThrowIfNegative(subtotal);
    // Tax rate and discount validation might belong in configuration/business rules

    return (subtotal * (1 + taxRate)) - discount;
}
```

## Rule 5: Use Specific Methods

Prefer specific methods over generic conditions:

```csharp
// Bad: Generic condition
Ensure.ThrowIfFalse(email.Contains("@"), "Invalid email");

// Good: Specific method with proper validation
Ensure.ThrowIfNotEmail(email);
```

## Rule 6: Combine Guards Efficiently

Group related guards together:

```csharp
public class Customer
{
    public Customer(Guid id, string name, string email, int age, Address address)
    {
        // Identity
        Ensure.ThrowIfEmpty(id);

        // Contact info
        Ensure.ThrowIfNullOrWhiteSpace(name);
        Ensure.ThrowIfNotEmail(email);

        // Demographics
        Ensure.ThrowIfOutOfRange(age, 0, 150);

        // Nested objects
        Ensure.ThrowIfNull(address);

        // Assignments
        Id = id;
        Name = name;
        Email = email;
        Age = age;
        Address = address;
    }
}
```

## Common Patterns

### Constructor Initialization

```csharp
public class Service
{
    private readonly IDependency _dependency;

    public Service(IDependency dependency)
    {
        Ensure.ThrowIfNull(dependency);
        _dependency = dependency;
    }
}
```

### Builder Pattern

```csharp
public OrderBuilder WithCustomer(Customer customer)
{
    Ensure.ThrowIfNull(customer);
    _customer = customer;
    return this;
}

public Order Build()
{
    Ensure.ThrowIfNull(_customer);
    Ensure.ThrowIfNullOrEmpty(_items);
    return new Order(_customer, _items);
}
```

### Optional Processing

```csharp
public void ProcessOptionalData(string? data)
{
    if (!Ensure.IsNotNullOrWhiteSpace(data))
        return;

    // Process non-null, non-whitespace data
    Process(data);  // data is known to be non-null here
}
```

## Anti-Patterns to Avoid

### Don't Catch Guard Exceptions

Guards indicate bugs. Don't catch and hide them:

```csharp
// Bad: Hiding bugs
try
{
    var service = new OrderService(null!);
}
catch (ArgumentNullException)
{
    return DefaultService();  // Don't do this!
}
```

### Don't Guard Internal State

Trust your own invariants:

```csharp
public class Order
{
    private readonly List<OrderItem> _items = new();

    public void AddItem(OrderItem item)
    {
        Ensure.ThrowIfNull(item);
        _items.Add(item);
    }

    public decimal GetTotal()
    {
        // Don't need to guard _items—we know it's never null
        return _items.Sum(i => i.Price);
    }
}
```
