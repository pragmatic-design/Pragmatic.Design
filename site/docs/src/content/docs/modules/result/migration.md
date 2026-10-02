---
title: "Migration Guide"
description: "This guide helps you migrate from traditional exception-based error handling or other Result libraries to Pragmatic.Result."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Result/docs/migration.md
sidebar:
  order: 7
---
This guide helps you migrate from traditional exception-based error handling or other Result libraries to Pragmatic.Result.

## From Exception-Based Code

### Before (Exceptions)
```csharp
public User GetUser(int id)
{
    var user = _db.Users.Find(id);
    if (user == null)
        throw new NotFoundException($"User {id} not found");
    return user;
}

// Caller must use try-catch
try
{
    var user = GetUser(123);
    Console.WriteLine(user.Name);
}
catch (NotFoundException ex)
{
    Console.WriteLine("User not found");
}
```

### After (Result)
```csharp
public Result<User, NotFoundError> GetUser(int id)
{
    var user = _db.Users.Find(id);
    if (user == null)
        return NotFoundError.Create("User", id);
    return user;
}

// Caller uses Match or pattern matching
var user = GetUser(123);
user.Match(
    u => Console.WriteLine(u.Name),
    e => Console.WriteLine("User not found"));

// Or using TryGetValue
if (user.TryGetValue(out var u))
    Console.WriteLine(u.Name);
```

## Key Migration Patterns

### 1. Return Type Changes

| Before | After |
|--------|-------|
| `User GetUser()` | `Result<User, NotFoundError> GetUser()` |
| `void DeleteUser()` | `VoidResult<NotFoundError> DeleteUser()` |
| `User? GetUserOrNull()` | `Maybe<User> GetUser()` |

### 2. Exception to Error Type Mapping

| Exception | Error Type |
|-----------|------------|
| `NotFoundException` | `NotFoundError` |
| `UnauthorizedAccessException` | `UnauthorizedError` |
| `InvalidOperationException` | `ForbiddenError` or `BadRequestError` |
| `DuplicateKeyException` | `ConflictError` |
| `ArgumentException` | `BadRequestError` |

### 3. Repository Pattern

#### Before
```csharp
public interface IUserRepository
{
    User GetById(int id);  // throws NotFoundException
    User? FindById(int id);  // returns null
    void Add(User user);  // throws ConflictException
}
```

#### After
```csharp
public interface IUserRepository
{
    Result<User, NotFoundError> GetById(int id);
    Maybe<User> FindById(int id);
    VoidResult<ConflictError> Add(User user);
}
```

### 4. Service Layer

#### Before
```csharp
public class OrderService
{
    public Order PlaceOrder(OrderRequest request)
    {
        if (!ValidateRequest(request))
            throw new ValidationException("Invalid request");

        var user = _userRepo.GetById(request.UserId);
        if (!user.CanPlaceOrders)
            throw new ForbiddenException("User cannot place orders");

        var order = new Order(user, request.Items);
        _orderRepo.Add(order);
        return order;
    }
}
```

#### After
```csharp
public class OrderService
{
    public Result<Order, PlaceOrderError> PlaceOrder(OrderRequest request)
    {
        return ValidateRequest(request)
            .Bind(_ => _userRepo.GetById(request.UserId)
                .MapError(e => PlaceOrderError.UserNotFound(e)))
            .Ensure(
                user => user.CanPlaceOrders,
                _ => PlaceOrderError.UserCannotOrder())
            .Map(user => new Order(user, request.Items))
            .Tap(order => _orderRepo.Add(order));
    }
}
```

### 5. Controller/API Layer

#### Before
```csharp
[HttpGet("{id}")]
public ActionResult<User> GetUser(int id)
{
    try
    {
        var user = _userService.GetUser(id);
        return Ok(user);
    }
    catch (NotFoundException)
    {
        return NotFound();
    }
    catch (UnauthorizedException)
    {
        return Unauthorized();
    }
}
```

#### After (Automatic Handling)
```csharp
// Register filter in Program.cs
builder.Services.AddControllers(options =>
{
    options.Filters.Add<ResultActionFilter>();
});

// Controller returns Result directly
[HttpGet("{id}")]
public async Task<Result<User, NotFoundError>> GetUser(int id)
    => await _userService.GetUserAsync(id);
// Automatic: 200 OK with user, or 404 ProblemDetails
```

## From Other Result Libraries

### From ErrorOr

| ErrorOr | Pragmatic.Result |
|---------|------------------|
| `ErrorOr<T>` | `Result<T, IError>` or `Result<T, E1, E2, ...>` |
| `.ThenDo(action)` | `.Tap(action)` or `.OnSuccess(action)` |
| `.FailIf(pred, error)` | `.Ensure(pred, errorFactory)` |
| `.Value` | `.Value` |
| `.Errors` | `.Error` (single) or pattern match |

```csharp
// ErrorOr
ErrorOr<User> GetUser() => ...;
var result = GetUser()
    .ThenDo(u => Log(u))
    .FailIf(u => !u.IsActive, Error.Validation("Inactive"));

// Pragmatic.Result
Result<User, IError> GetUser() => ...;
var result = GetUser()
    .Tap(u => Log(u))
    .Ensure(u => u.IsActive, _ => new InactiveUserError());
```

### From FluentResults

| FluentResults | Pragmatic.Result |
|---------------|------------------|
| `Result<T>` | `Result<T, TError>` |
| `.IsSuccess` | `.IsSuccess` |
| `.Value` | `.Value` |
| `.Errors` | `.Error` |
| `.OnSuccess(action)` | `.OnSuccess(action)` |
| `.OnFailure(action)` | `.OnFailure(action)` |
| `.Bind(func)` | `.Bind(func)` |

**Key difference**: Pragmatic.Result uses typed errors, not `List<IError>`.

### From OneOf

| OneOf | Pragmatic.Result |
|-------|------------------|
| `OneOf<T, Error>` | `Result<T, Error>` |
| `.Match(...)` | `.Match(...)` |
| `.AsT0` | `.Value` |
| `.IsT0` | `.IsSuccess` |

```csharp
// OneOf
OneOf<User, NotFound, Forbidden> GetUser();
result.Match(
    user => Ok(user),
    notFound => NotFound(),
    forbidden => Forbid());

// Pragmatic.Result (with multi-error)
Result<User, NotFoundError, ForbiddenError> GetUser();
result.Match<IActionResult>(
    user => Ok(user),
    notFound => NotFound(),
    forbidden => Forbid());
```

## Bridging with Existing Code

### Result.Try for Legacy Integration

Use `Result.Try` and `Result.TryAsync` to bridge existing exception-throwing code:

```csharp
// Wrap legacy code
public Result<Data, BadRequestError> GetLegacyData(string id)
{
    return Result.Try(
        () => _legacyService.GetData(id),
        ex => BadRequestError.Create(ex.Message));
}

// Async version -- the operation receives a CancellationToken
public Task<Result<Data, BadRequestError>> GetLegacyDataAsync(string id, CancellationToken cancellationToken)
{
    return Result.TryAsync(
        ct => _legacyService.GetDataAsync(id, ct),
        ex => BadRequestError.Create(ex.Message),
        cancellationToken);
}
```

For void operations, the same factories return `VoidResult<TError>`:

```csharp
public VoidResult<BadRequestError> SaveLegacyData(Data data)
{
    return Result.Try(
        () => _legacyService.Save(data),
        ex => BadRequestError.Create(ex.Message));
}
```

### Gradual Migration Strategy

1. **Start at boundaries**: Convert API endpoints first
2. **Work inward**: Migrate services, then repositories
3. **Use Result.Try**: Bridge legacy code during transition
4. **Update tests**: Convert exception assertions to Result assertions

## When NOT to Use Result

Keep using exceptions for:

- **Programming errors** (bugs): `ArgumentNullException`, `IndexOutOfRangeException`
- **Infrastructure failures**: Database connection lost, file system errors
- **Unrecoverable states**: Out of memory, stack overflow

Use Result for:

- **Business rule violations**: User not found, insufficient permissions
- **Validation failures**: Invalid input, constraint violations
- **Expected failures**: Cache miss, optional data not available

## Testing Migration

### Before
```csharp
[Fact]
public void GetUser_NotFound_ThrowsException()
{
    var service = new UserService();

    Assert.Throws<NotFoundException>(() =>
        service.GetUser(999));
}
```

### After
```csharp
[Fact]
public void GetUser_NotFound_ReturnsFailure()
{
    var service = new UserService();

    var result = service.GetUser(999);

    result.IsFailure.Should().BeTrue();
    result.Error.Code.Should().Be("NOT_FOUND");
}
```

## Need Help?

- [Getting Started](/modules/result/getting-started/)
- [API Reference](/modules/result/api-reference/)
- [Troubleshooting](/modules/result/troubleshooting/)
