# Getting Started with Pragmatic.Ensure

This guide will get you up and running with Pragmatic.Ensure in minutes.

## Installation

```bash
# Core package (ThrowIf and Is patterns)
dotnet add package Pragmatic.Ensure

# Optional: Result integration (Check pattern for domain validation)
dotnet add package Pragmatic.Ensure.Result
```

## Quick Start

### ThrowIf Pattern (Constructor Guards)

The most common use case is validating constructor parameters:

```csharp
using Pragmatic.Ensure;

public class Order
{
    public Guid Id { get; }
    public string CustomerEmail { get; }
    public decimal Amount { get; }
    public IReadOnlyList<OrderItem> Items { get; }

    public Order(Guid id, string customerEmail, decimal amount, IReadOnlyList<OrderItem> items)
    {
        // Guards automatically capture parameter names via CallerArgumentExpression
        Ensure.ThrowIfEmpty(id);                    // ArgumentException if Guid.Empty
        Ensure.ThrowIfNotEmail(customerEmail);      // ArgumentException if invalid email
        Ensure.ThrowIfNegativeOrZero(amount);       // ArgumentOutOfRangeException if <= 0
        Ensure.ThrowIfNullOrEmpty(items);           // ArgumentNullException or ArgumentException

        Id = id;
        CustomerEmail = customerEmail;
        Amount = amount;
        Items = items;
    }
}
```

### Is Pattern (Conditional Logic)

For validation without exceptions:

```csharp
using Pragmatic.Ensure;

public void ProcessUser(string? name, string? email)
{
    // Null-state analysis works correctly
    if (Ensure.IsNotNullOrWhiteSpace(name))
    {
        // 'name' is known to be non-null here
        Console.WriteLine($"Processing: {name}");
    }

    // Combine checks
    if (Ensure.IsEmail(email) && Ensure.IsLengthInRange(email, 5, 254))
    {
        SendEmail(email);
    }
}
```

## Three Patterns, Three Purposes

| Pattern | Package | Purpose | Example |
|---------|---------|---------|---------|
| `ThrowIf*` | Pragmatic.Ensure | Programming errors (bugs) | Constructor guards, method preconditions |
| `Is*` | Pragmatic.Ensure | Boolean checks | Conditional flows, quick validation |
| `Check.*` | Pragmatic.Ensure.Result | Domain validation | User input, business rules with typed errors |

### When to Use ThrowIf

- Constructor parameter validation
- Public API method preconditions
- Invariant checking
- Any situation where invalid data indicates a bug

```csharp
public void Transfer(Account from, Account to, decimal amount)
{
    Ensure.ThrowIfNull(from);
    Ensure.ThrowIfNull(to);
    Ensure.ThrowIfNegativeOrZero(amount);
    Ensure.ThrowIfFalse(from.Balance >= amount, "Insufficient funds");

    // ... proceed with transfer
}
```

### When to Use Is

- Conditional branching
- User input filtering
- Optional processing
- Any situation where invalid data is expected and handled

```csharp
public void ImportContacts(IEnumerable<ContactDto> contacts)
{
    foreach (var contact in contacts)
    {
        if (!Ensure.IsEmail(contact.Email))
        {
            _logger.LogWarning("Skipping invalid email: {Email}", contact.Email);
            continue;
        }

        ProcessContact(contact);
    }
}
```

### When to Use Check (Result Pattern)

- Domain validation where failure is **expected** (user input, business rules)
- When you need **typed errors** with rich information
- Composable validation chains with `Then()`
- Integration with the Result pattern

```csharp
using Pragmatic.Ensure.Result;

public VoidResult<ValidationError> ValidateUser(UserDto dto)
{
    return Check.NotNullOrWhiteSpace(dto.Name, ValidationError.For("Name", "validation.required"))
        .Then(() => Check.Email(dto.Email, ValidationError.For("Email", "validation.email")))
        .Then(() => Check.InRange(dto.Age, 0, 150, ValidationError.For("Age", "validation.range", ("min", 0), ("max", 150))));
}

// Chain validations with typed errors
var result = ValidateUser(userDto);
if (result.IsFailure)
{
    return BadRequest(result.Error.Issues);   // property path + message key per issue
}
```

## Automatic Parameter Names

Thanks to `[CallerArgumentExpression]`, you never need to specify parameter names:

```csharp
public void Process(string customerName, int orderId)
{
    Ensure.ThrowIfNullOrWhiteSpace(customerName);
    // Throws: ArgumentException: Value cannot be null or whitespace. (Parameter 'customerName')

    Ensure.ThrowIfNegative(orderId);
    // Throws: ArgumentOutOfRangeException: Value cannot be negative. (Parameter 'orderId')
}
```

## Common Validation Scenarios

### Entity Validation

```csharp
public class User
{
    public User(Guid id, string name, string email, int age)
    {
        Ensure.ThrowIfEmpty(id);
        Ensure.ThrowIfNullOrWhiteSpace(name);
        Ensure.ThrowIfNotEmail(email);
        Ensure.ThrowIfOutOfRange(age, 0, 150);

        // ... assign properties
    }
}
```

### Service Method Guards

```csharp
public async Task<Order> CreateOrder(CreateOrderRequest request)
{
    Ensure.ThrowIfNull(request);
    Ensure.ThrowIfNullOrEmpty(request.Items);
    Ensure.ThrowIfNegativeOrZero(request.TotalAmount);

    // ... create order
}
```

### Collection Operations

```csharp
public void ProcessBatch(IEnumerable<Item> items)
{
    Ensure.ThrowIfNullOrEmpty(items);  // Checks null and empty

    foreach (var item in items)
    {
        // Safe to enumerate
    }
}
```

## Next Steps

- [API Reference](api-reference.md) - Complete method documentation
- [Best Practices](best-practices.md) - Advanced patterns and tips
