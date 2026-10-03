---
title: "Common Mistakes"
description: "These are the most common issues developers encounter when using Pragmatic.Ensure. Each section shows the wrong approach, the correct approach, and explains why"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Ensure/docs/common-mistakes.md
sidebar:
  order: 5
---
These are the most common issues developers encounter when using Pragmatic.Ensure. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Using Manual Throws Instead of Ensure

**Wrong:**

```csharp
public class OrderService
{
    private readonly IOrderRepository _repository;

    public OrderService(IOrderRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public void Process(string email, int quantity)
    {
        if (email is null) throw new ArgumentNullException(nameof(email));
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email required", nameof(email));
        if (!email.Contains("@")) throw new ArgumentException("Invalid email", nameof(email));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
    }
}
```

**Right:**

```csharp
public class OrderService(IOrderRepository repository)
{
    private readonly IOrderRepository _repository = Ensure.ThrowIfNull(repository);

    public void Process(string email, int quantity)
    {
        Ensure.ThrowIfNullOrWhiteSpace(email);
        Ensure.ThrowIfNotEmail(email);
        Ensure.ThrowIfNegativeOrZero(quantity);
    }
}
```

**Why:** Manual throws require you to pick the right exception type, write the right message, and pass the right parameter name -- every time. `Ensure.ThrowIf*` handles all three consistently. Parameter names are captured automatically via `[CallerArgumentExpression]`, so they survive renames. The email validation uses `System.Net.Mail.MailAddress` internally, which is more robust than `email.Contains("@")`.

---

## 2. Using ThrowIf for Business Validation

**Wrong:**

```csharp
public User GetUser(int id)
{
    var user = _repository.FindById(id);
    Ensure.ThrowIfNull(user); // "User not found" is not a bug!
    return user;
}

public void TransferMoney(Account from, decimal amount)
{
    Ensure.ThrowIfTrue(from.Balance < amount, "Insufficient funds");
    // Insufficient funds is a business rule, not a programming error
}
```

**Right:**

```csharp
public Result<User, NotFoundError> GetUser(int id)
{
    var user = _repository.FindById(id);
    if (user is null)
        return NotFoundError.For("User", id);
    return user;
}

public VoidResult<InsufficientFundsError> TransferMoney(Account from, decimal amount)
{
    var check = Check.That(from.Balance >= amount, new InsufficientFundsError(from.Id, amount));
    if (check.IsFailure)
        return check.Error;
    // proceed
    return VoidResult<InsufficientFundsError>.Success();
}
```

**Why:** `ThrowIf*` methods are for **programming errors** -- situations that should never occur if the caller is correct. "User not found" and "insufficient funds" are expected business outcomes. Using exceptions for these means: (1) the caller has to catch exceptions for normal flow, (2) the error path is invisible to the type system, and (3) in an HTTP context, unhandled exceptions produce 500 instead of the correct status code (404, 422). Use the Result pattern or `Check.*` for expected failures.

---

## 3. Guarding Private Methods That Are Already Validated

**Wrong:**

```csharp
public class OrderProcessor
{
    public void ProcessOrder(Order order)
    {
        Ensure.ThrowIfNull(order);
        Ensure.ThrowIfNullOrEmpty(order.Items);
        ProcessItems(order.Items);
    }

    private void ProcessItems(IReadOnlyList<OrderItem> items)
    {
        // Redundant! Already validated in the public caller.
        Ensure.ThrowIfNullOrEmpty(items);

        foreach (var item in items)
        {
            // Also redundant!
            Ensure.ThrowIfNull(item);
            ProcessSingleItem(item);
        }
    }

    private void ProcessSingleItem(OrderItem item)
    {
        // Triple-checked by now
        Ensure.ThrowIfNull(item);
        // ...
    }
}
```

**Right:**

```csharp
public class OrderProcessor
{
    public void ProcessOrder(Order order)
    {
        // Guard at the public boundary
        Ensure.ThrowIfNull(order);
        Ensure.ThrowIfNullOrEmpty(order.Items);
        ProcessItems(order.Items);
    }

    private void ProcessItems(IReadOnlyList<OrderItem> items)
    {
        // Trust the public method's validation
        foreach (var item in items)
        {
            ProcessSingleItem(item);
        }
    }

    private void ProcessSingleItem(OrderItem item)
    {
        // No guard needed -- caller validated
    }
}
```

**Why:** Guard at the **public boundary**, trust your own invariants internally. Redundant guards in private methods add noise without safety. If a private method can only be reached through a public method that already validated the input, the guard is dead code. The exception: if a private method is called from multiple public methods with different validation paths, guard at the private method level.

---

## 4. Using ThrowIfFalse/ThrowIfTrue for Everything

**Wrong:**

```csharp
public void CreateUser(string email, int age, Guid departmentId)
{
    Ensure.ThrowIfFalse(email != null, "Email required");
    Ensure.ThrowIfFalse(email.Contains("@"), "Invalid email");
    Ensure.ThrowIfFalse(age >= 0 && age <= 150, "Invalid age");
    Ensure.ThrowIfTrue(departmentId == Guid.Empty, "Department required");
}
```

**Right:**

```csharp
public void CreateUser(string email, int age, Guid departmentId)
{
    Ensure.ThrowIfNullOrWhiteSpace(email);
    Ensure.ThrowIfNotEmail(email);
    Ensure.ThrowIfOutOfRange(age, 0, 150);
    Ensure.ThrowIfEmpty(departmentId);
}
```

**Why:** Specific methods produce better exception messages, use the correct exception types (`ArgumentOutOfRangeException` for range violations, `ArgumentException` for format issues), and communicate intent more clearly. `ThrowIfFalse(email.Contains("@"))` produces a generic message like "Condition must be true (Parameter 'email.Contains("@")')". `ThrowIfNotEmail(email)` produces "Value must be a valid email address (Parameter 'email')". Reserve `ThrowIfTrue`/`ThrowIfFalse` for truly custom conditions that no specific method covers.

---

## 5. Catching Guard Exceptions for Control Flow

**Wrong:**

```csharp
public OrderService CreateService(IOrderRepository? repository)
{
    try
    {
        return new OrderService(repository!);
        // OrderService constructor calls Ensure.ThrowIfNull
    }
    catch (ArgumentNullException)
    {
        return new OrderService(new InMemoryOrderRepository());
    }
}
```

**Right:**

```csharp
public OrderService CreateService(IOrderRepository? repository)
{
    repository ??= new InMemoryOrderRepository();
    return new OrderService(repository);
}
```

**Why:** Guard exceptions signal bugs. Catching them means you know the input might be invalid -- in which case it is not a bug, it is expected behavior, and you should handle it with normal control flow (null coalescing, `if` checks, `Is*` methods, or the Result pattern). Catching `ArgumentNullException` as a branching mechanism is exception-driven control flow, which is slow, hard to read, and hides the intent.

---

## 6. Ignoring Null-Safety Contracts on String Methods

**Wrong:**

```csharp
public void UpdateProfile(string? phone, string? website)
{
    // Developer expects ThrowIfNotPhone to throw on null
    Ensure.ThrowIfNotPhone(phone);   // Returns silently if phone is null!
    Ensure.ThrowIfNotUrl(website);   // Returns silently if website is null!

    // Bug: phone and website might still be null here
    _profile.Phone = phone;
    _profile.Website = website;
}
```

**Right:**

```csharp
public void UpdateProfile(string? phone, string? website)
{
    // Option A: If phone/website are required, check null first
    Ensure.ThrowIfNullOrWhiteSpace(phone);
    Ensure.ThrowIfNotPhone(phone);

    // Option B: If phone/website are optional, the null-safety is intentional
    Ensure.ThrowIfNotPhone(phone);   // null is OK -- optional field
    Ensure.ThrowIfNotUrl(website);   // null is OK -- optional field
    _profile.Phone = phone;          // Allowed to be null
    _profile.Website = website;
}
```

**Why:** The format validation methods (`ThrowIfNotEmail`, `ThrowIfNotPhone`, `ThrowIfNotUrl`, `ThrowIfNotCreditCard`, `ThrowIfNotMatch`) are **null-safe** -- they return without throwing when the value is null. This is by design for optional fields. (Note: `ThrowIfLengthOutOfRange`, `ThrowIfLongerThan`, and `ThrowIfShorterThan` are **not** null-safe -- they throw `ArgumentNullException` on null.) If the field is required, validate presence first with `ThrowIfNullOrWhiteSpace`, then validate format. See the [Concepts](/modules/ensure/concepts/) guide for the full null-safety contract table.

---

## 7. Using Is* Methods Without Handling the False Case

**Wrong:**

```csharp
public void SendEmail(string email)
{
    Ensure.IsEmail(email); // Returns bool, but the result is discarded!
    _mailer.Send(email);   // Sends to invalid email addresses
}
```

**Right:**

```csharp
// Option A: Guard, if invalid email is a bug
public void SendEmail(string email)
{
    Ensure.ThrowIfNotEmail(email);
    _mailer.Send(email);
}

// Option B: Branch, if invalid email is expected
public void SendEmail(string? email)
{
    if (!Ensure.IsEmail(email))
    {
        _logger.LogWarning("Skipping invalid email: {Email}", email);
        return;
    }
    _mailer.Send(email);
}
```

**Why:** `Is*` methods return `bool`. Calling them without using the return value is a no-op -- the validation happens but the result is thrown away. If you want to enforce the constraint, use `ThrowIf*`. If you want to branch on it, use the `bool` return value in an `if` statement. The compiler does not warn about discarded `bool` returns by default, so this mistake can go unnoticed in code review.

---

## 8. Eager Error Construction in Hot Paths

**Wrong:**

```csharp
public VoidResult<ValidationError> ValidateItems(IReadOnlyList<OrderItem> items)
{
    foreach (var item in items)
    {
        // Error is constructed on EVERY iteration, even when validation passes
        var result = Check.NotNullOrWhiteSpace(item.Name,
            ValidationError.For($"Items[{items.IndexOf(item)}].Name", "validation.required"));

        if (result.IsFailure)
            return result;
    }
    return VoidResult<ValidationError>.Success();
}
```

**Right:**

```csharp
public VoidResult<ValidationError> ValidateItems(IReadOnlyList<OrderItem> items)
{
    for (var i = 0; i < items.Count; i++)
    {
        var item = items[i];
        // Error is constructed only when validation fails
        var index = i; // capture for lambda
        var result = Check.NotNullOrWhiteSpace(item.Name,
            () => ValidationError.For($"Items[{index}].Name", "validation.required"));

        if (result.IsFailure)
            return result;
    }
    return VoidResult<ValidationError>.Success();
}
```

**Why:** Every `Check.*` method has a `Func<TError>` overload (lazy error construction). The factory is only invoked when validation fails. In a loop over 1000 items where 999 pass, the eager version constructs (and immediately discards) 999 error objects with string interpolation. The lazy version allocates nothing until a failure occurs. Use lazy construction when: (1) the error involves string interpolation, (2) the check runs in a loop, or (3) the success rate is high.

---

## 9. Mixing Ensure and Check for the Same Validation

**Wrong:**

```csharp
public Result<Order, ValidationError> CreateOrder(CreateOrderDto dto)
{
    // Mixed paradigms: half throws, half returns errors
    Ensure.ThrowIfNull(dto);                          // Throws
    Ensure.ThrowIfNullOrEmpty(dto.Items);             // Throws

    var emailCheck = Check.Email(dto.Email,           // Returns Result
        ValidationError.For("Email", "validation.email"));
    if (emailCheck.IsFailure)
        return emailCheck.Error;

    Ensure.ThrowIfNegativeOrZero(dto.TotalAmount);    // Throws again

    return new Order(dto);
}
```

**Right:**

```csharp
public Result<Order, ValidationError> CreateOrder(CreateOrderDto dto)
{
    // Guards for programming errors (caller contract)
    Ensure.ThrowIfNull(dto);

    // Check for business validation (user input)
    var validation = Check.NotNullOrEmpty(dto.Items,
            ValidationError.For("Items", "validation.notempty"))
        .Then(() => Check.Email(dto.Email,
            ValidationError.For("Email", "validation.email")))
        .Then(() => Check.That(dto.TotalAmount > 0,
            ValidationError.For("TotalAmount", "validation.positive")));

    if (validation.IsFailure)
        return validation.Error;

    return new Order(dto);
}
```

**Why:** The two patterns serve different purposes. `ThrowIf*` is for **contract violations** (the DTO being null means the caller has a bug). `Check.*` is for **input validation** (empty items, invalid email, bad amounts are expected failures from user input). Do not alternate between them for the same category of validation. Use `ThrowIf*` once at the top for preconditions, then `Check.*` for all business rules.

---

## 10. Assuming ThrowIfEmpty on a Collection Also Checks for Null

**Wrong:**

```csharp
public void Process(Guid orderId, IReadOnlyList<OrderItem>? items)
{
    Ensure.ThrowIfEmpty(orderId);  // Checks Guid.Empty, fine
    Ensure.ThrowIfEmpty(items!);   // Does NOT check null: a null 'items' throws NullReferenceException
}
```

**Right:**

```csharp
public void Process(Guid orderId, IReadOnlyList<OrderItem>? items)
{
    Ensure.ThrowIfEmpty(orderId);       // Checks Guid.Empty (Guid is a struct, never null)
    Ensure.ThrowIfNullOrEmpty(items);   // Checks BOTH null and empty in one call
}
```

**Why:** `ThrowIfEmpty(Guid)` checks for `Guid.Empty`. `ThrowIfEmpty(IEnumerable<T>)` checks **only** for emptiness -- it does *not* null-check, so a null argument throws `NullReferenceException` (an ugly, unhelpful failure). `ThrowIfNullOrEmpty` is a **different** method that guards null (`ArgumentNullException`) *and* empty (`ArgumentException`) -- it is not an alias. Use `ThrowIfNullOrEmpty` for a required collection; reach for the bare `ThrowIfEmpty` only when you have already guaranteed non-null.

---

## 11. Using Check.* Without Pragmatic.Ensure.Result Package

**Wrong:**

```csharp
using Pragmatic.Ensure.Result; // Compile error: namespace not found

var result = Check.NotNull(user, NotFoundError.For("User", userId));
```

**Build result:** `CS0246` -- The type or namespace name 'Check' could not be found.

**Right:**

```bash
# Install the Result integration package
dotnet add package Pragmatic.Ensure.Result
```

```csharp
using Pragmatic.Ensure.Result;

var result = Check.NotNull(user, NotFoundError.For("User", userId));
```

**Why:** `Check.*` methods live in the `Pragmatic.Ensure.Result` package, not in `Pragmatic.Ensure`. The core package has zero dependencies. The Result integration is a separate package because it depends on `Pragmatic.Result` (for `VoidResult<TError>` and `IError`). If you only need `ThrowIf*` and `Is*`, you do not need the Result package.

---

## 12. Not Leveraging ThrowIfNull's Return Value

**Wrong:**

```csharp
public class NotificationService
{
    private readonly IEmailSender _emailSender;
    private readonly ISmsSender _smsSender;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IEmailSender emailSender,
        ISmsSender smsSender,
        ILogger<NotificationService> logger)
    {
        Ensure.ThrowIfNull(emailSender);
        Ensure.ThrowIfNull(smsSender);
        Ensure.ThrowIfNull(logger);

        _emailSender = emailSender;
        _smsSender = smsSender;
        _logger = logger;
    }
}
```

**Right:**

```csharp
public class NotificationService(
    IEmailSender emailSender,
    ISmsSender smsSender,
    ILogger<NotificationService> logger)
{
    private readonly IEmailSender _emailSender = Ensure.ThrowIfNull(emailSender);
    private readonly ISmsSender _smsSender = Ensure.ThrowIfNull(smsSender);
    private readonly ILogger<NotificationService> _logger = Ensure.ThrowIfNull(logger);
}
```

**Why:** `ThrowIfNull` returns the validated non-null value. Combined with primary constructors, this eliminates the constructor body entirely -- each field is validated and assigned in a single expression. The result is less code with the same safety guarantee.

---

## Quick Reference

| Mistake | Symptom |
|---------|---------|
| Manual throws | Inconsistent messages, fragile `nameof`, verbose code |
| ThrowIf for business logic | 500 instead of typed error, exception-driven flow |
| Guarding private methods | Dead code, noise, false sense of extra safety |
| ThrowIfFalse for everything | Generic messages, wrong exception types |
| Catching guard exceptions | Exception-driven control flow, hiding bugs |
| Ignoring null-safety contracts | Null values slip through format validations |
| Discarding Is* return value | Validation runs but is never checked |
| Eager error in hot paths | Unnecessary allocations on every iteration |
| Mixing Ensure and Check | Inconsistent error handling for the same category |
| Redundant null + empty checks | Extra guard calls that are already covered |
| Missing Ensure.Result package | Compile error on `Check.*` methods |
| Not using ThrowIfNull return | Unnecessary constructor body, extra lines |
