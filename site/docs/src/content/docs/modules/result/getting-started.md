---
title: "Getting Started with Pragmatic.Result"
description: "This guide will get you up and running with Pragmatic.Result in 5 minutes."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Result/docs/getting-started.md
sidebar:
  order: 2
---
This guide will get you up and running with Pragmatic.Result in 5 minutes.

## Installation

```bash
dotnet add package Pragmatic.Result
dotnet add package Pragmatic.Result.AspNetCore  # For ASP.NET Core integration
dotnet add package Pragmatic.Result.Analyzers   # PRAG0001: warns on unsafe .Value access
```

## Quick Start

### 1. Basic Result Usage

```csharp
using Pragmatic.Result;
using Pragmatic.Result.Http;

public class UserService
{
    public Result<User, NotFoundError> GetUser(int id)
    {
        var user = _db.Users.Find(id);
        if (user is null)
            return NotFoundError.Create("User", id);

        return user;  // Implicit conversion to Success
    }
}
```

### 2. Handle Results

```csharp
var result = userService.GetUser(42);

// Option 1: Pattern matching with Match
var response = result.Match(
    user => $"Hello, {user.Name}!",
    error => $"Error: {error.Code}"
);

// Option 2: TryGet pattern
if (result.TryGetValue(out var user))
{
    Console.WriteLine($"Found: {user.Name}");
}

// Option 3: Check IsSuccess/IsFailure
if (result.IsSuccess)
{
    var user = result.Value;
}
```

### 3. Pipeline Operations

When a pipeline mixes failure modes (not found, business rule), type the result over `IError`:

```csharp
using Pragmatic.Result.Extensions;  // Map, Ensure, Tap, OnFailure, ...

public Result<UserDto, IError> GetActiveUser(int id)
{
    Result<User, IError> result = _db.Users.Find(id) is { } user
        ? user
        : NotFoundError.Create("User", id);

    return result
        .Ensure(u => u.IsActive, u => BusinessRuleError.Inactive("User"))
        .Map(u => u.ToDto())
        .Tap(dto => _logger.LogInformation("User retrieved: {Id}", dto.Id))
        .OnFailure(e => _logger.LogWarning("Failed: {Code}", e.Code));
}
```

Each step runs only while the result is a success; the first failure short-circuits the rest.

### 4. ASP.NET Core Integration

Register the services, then enable automatic Result-to-HTTP conversion.

**Minimal APIs** -- add `WithResultHandling()` to a route group:

```csharp
// Program.cs
using Pragmatic.Result.AspNetCore;

builder.Services.AddPragmaticResult();

var app = builder.Build();

app.MapGroup("").WithResultHandling()
   .MapGet("/users/{id}", async (int id, UserService svc) => await svc.GetUserAsync(id));
// Returns 200 OK with user, or 404 ProblemDetails automatically
// VoidResult success -> 204 No Content
```

**Controllers** -- add `ResultActionFilter` as a global filter:

```csharp
// Program.cs
builder.Services.AddPragmaticResult();
builder.Services.AddControllers(options => options.Filters.Add<ResultActionFilter>());

// Controller
[HttpGet("{id}")]
public async Task<Result<UserDto, NotFoundError>> GetUser(int id)
{
    return await _userService.GetUserAsync(id);
}
// Returns 200 OK with user, or 404 ProblemDetails automatically
```

Opt out a specific endpoint with `[SkipResultHandling]`.

## Core Concepts

### Result Types

| Type | Use Case |
|------|----------|
| `Result<TValue, TError>` | Operations that return a value or fail |
| `Result<TValue, TError1, ..., TError8>` | Multiple distinct failure modes (2-8 error types) |
| `VoidResult<TError>` | Operations that succeed or fail (no value) |
| `Maybe<T>` | Optional values (Some or None) |

### Error Types

All in the `Pragmatic.Result.Http` namespace:

| Error | HTTP Status | Use Case |
|-------|-------------|----------|
| `BadRequestError` | 400 | Malformed request, missing header |
| `UnauthorizedError` | 401 | Authentication required |
| `ForbiddenError` | 403 | Insufficient permissions |
| `NotFoundError` | 404 | Entity not found |
| `ConflictError` | 409 | Duplicate key, concurrency |
| `BusinessRuleError` | 422 | Business rule prevents processing |
| `InternalServerError` | 500 | Unexpected failure |
| `DependencyError` | 502/503/504 | External service failure (transient) |

### Key Methods

```csharp
// Transformation
result.Map(x => Transform(x))           // Transform success value
result.MapError(e => NewError(e))       // Transform error
result.Bind(x => GetOther(x))           // Chain operations

// Side effects (don't change result)
result.Tap(x => Log(x))                 // Execute on success
result.OnSuccess(x => Notify(x))        // Alias for Tap
result.OnFailure(e => LogError(e))      // Execute on failure

// Validation
result.Ensure(x => x.IsValid, x => BadRequestError.Create("Invalid"))

// Recovery
result.OrElse(e => FallbackResult())     // Fallback Result on failure
result.Recover(e => DefaultValue())      // Fallback value on failure (always Success)
result.GetValueOrDefault(fallback)       // Get value or fallback
```

## Multi-Error Types

For operations that can fail in different ways. Variants for 2 to 8 error types ship with the package; each error type gets its own `Match` handler:

```csharp
public Result<User, NotFoundError, ForbiddenError> GetUser(int id, ClaimsPrincipal user)
{
    var entity = _db.Users.Find(id);
    if (entity is null)
        return NotFoundError.Create("User", id);

    if (!user.CanView(entity))
        return new ForbiddenError();

    return entity;
}

// Handle with Match
result.Match(
    user => Ok(user),
    notFound => NotFound(),
    forbidden => Forbid()
);
```

## Async Operations

```csharp
var result = await GetUserAsync(id)
    .MapAsync(u => EnrichUserAsync(u))
    .BindAsync(u => ValidateAsync(u))
    .TapAsync(u => SendNotificationAsync(u));
```

## Best Practices

1. **Use specific error types** - `NotFoundError` over generic `Error`
2. **Prefer pipelines** - Chain operations with Map/Bind/Ensure
3. **Side effects in Tap** - Logging, metrics, notifications
4. **Validate early** - Use Ensure to add validation steps
5. **Handle all cases** - Use Match for exhaustive handling

## Next Steps

- [Architecture and Core Concepts](/modules/result/concepts/) - Why Result exists and how to choose the right type
- [API Reference](/modules/result/api-reference/) - Complete method documentation
- [Migration Guide](/modules/result/migration/) - Migrate from exceptions/other libraries
- [Localization](/modules/result/localization/) - Localize error messages
- [Troubleshooting](/modules/result/troubleshooting/) - Common issues and solutions
