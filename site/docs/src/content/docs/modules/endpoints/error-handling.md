---
title: "Error Handling"
description: "Complete guide to error handling and HTTP status mapping in Pragmatic.Endpoints."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Endpoints/docs/error-handling.md
sidebar:
  order: 6
---
Complete guide to error handling and HTTP status mapping in Pragmatic.Endpoints.

## Result Pattern

Pragmatic.Endpoints uses the Result pattern from `Pragmatic.Result` for type-safe error handling:

```csharp
[Endpoint(HttpVerb.Get, "/users/{id}")]
public partial class GetUserEndpoint : Endpoint<UserDto>
{
    private IUserRepository _users = null!;

    [FromRoute]
    public Guid Id { get; set; }

    public override async Task<Result<UserDto>> HandleAsync(CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(Id, ct);
        if (user is null)
            return NotFoundError.For("User", Id);  // Error case

        return new UserDto(user);  // Success case
    }
}
```

## Standard Error Types

`Pragmatic.Result.Http` provides standard error types with automatic HTTP status mapping; `ValidationError`
comes from `Pragmatic.Validation`. None has a public positional constructor — build them with their
factories:

| Error Type | HTTP Status | Use Case | Build with |
|------------|-------------|----------|------------|
| `NotFoundError` | 404 | Resource not found | `NotFoundError.For("User", id)` |
| `ValidationError` | 422 | Input understood, rules refuse it | `ValidationError.For("Email", "validation.email")` |
| `BusinessRuleError` | 422 | A domain rule refuses the operation | `BusinessRuleError.Create("order.not_pending")` |
| `BadRequestError` | 400 | Malformed request | `BadRequestError.InvalidParameter("page", "must be positive")` |
| `UnauthorizedError` | 401 | Authentication required | `UnauthorizedError.MissingToken()` |
| `ForbiddenError` | 403 | Insufficient permissions | `ForbiddenError.MissingPermission("users.edit")` |
| `ConflictError` | 409 | Duplicate/concurrency conflict | `ConflictError.AlreadyExists("Order", key)` |
| `InternalServerError` | 500 | Unexpected failure | `InternalServerError.From(exception)` |
| `DependencyError` | 502 / 503 / 504 | An external service failed | `DependencyError.Unavailable("payments")` |

### Using Standard Errors

```csharp
[Endpoint(HttpVerb.Post, "/orders")]
public partial class CreateOrderEndpoint : Endpoint<OrderDto>
{
    public override async Task<Result<OrderDto>> HandleAsync(CancellationToken ct)
    {
        // Validation error
        if (Items.Count == 0)
            return ValidationError.For("Items", "validation.mincount", ("min", 1));

        // Not found
        var customer = await _customers.GetAsync(CustomerId, ct);
        if (customer is null)
            return NotFoundError.For("Customer", CustomerId);

        // Conflict (duplicate)
        if (await _orders.ExistsAsync(IdempotencyKey, ct))
            return ConflictError.AlreadyExists("Order", IdempotencyKey);

        // Success
        var order = new Order(...);
        return new OrderDto(order);
    }
}
```

## Multiple Error Types

DomainAction endpoints can declare multiple error types:

```csharp
[Endpoint(HttpVerb.Put, "/users/{id}")]
public partial class UpdateUser : DomainAction<UserDto, NotFoundError, ValidationError, ForbiddenError>
{
    public override async Task<Result<UserDto, IError>> Execute(CancellationToken ct)
    {
        var user = await Users.GetByIdAsync(Id, ct);
        if (user is null)
            return NotFoundError.For("User", Id);  // → 404

        if (!CurrentUser.CanEdit(user))
            return ForbiddenError.ActionDenied("edit", "User");  // → 403

        ValidationError validation = user.Validate(Name, Email);
        if (validation.IsFailure)
            return validation;  // → 422

        return new UserDto(user);  // → 200
    }
}
```

The generator produces OpenAPI documentation for all error types:

```yaml
responses:
  200:
    description: Success
    content:
      application/json:
        schema:
          $ref: '#/components/schemas/UserDto'
  404:
    description: Not Found
    content:
      application/problem+json:
        schema:
          $ref: '#/components/schemas/ProblemDetails'
  403:
    description: Forbidden
  422:
    description: Validation Failed
```

## Custom Error Types

Derive from the `Error` record and declare it `partial`: the generator writes its public properties into
the ProblemDetails extensions (`WriteExtensions`), so the client receives them without any reflection.

```csharp
public sealed partial record InsufficientStockError : Error
{
    public override string Code => "INSUFFICIENT_STOCK";
    public override int StatusCode => 422;
    public override string Title => "Insufficient stock";

    public required Guid ProductId { get; init; }
    public required int Requested { get; init; }
    public required int Available { get; init; }
}

[Endpoint(HttpVerb.Post, "/orders")]
public partial class CreateOrder : DomainAction<OrderId, InsufficientStockError, ValidationError>
{
    public override async Task<Result<OrderId, IError>> Execute(CancellationToken ct)
    {
        foreach (var item in Items)
        {
            var stock = await Inventory.GetStockAsync(item.ProductId, ct);
            if (stock < item.Quantity)
                return new InsufficientStockError
                {
                    ProductId = item.ProductId, Requested = item.Quantity, Available = stock
                };
        }
        // ...
    }
}
```

### The status the contract documents

The generator writes an error's status into the endpoint's metadata (and so into OpenAPI and the client
manifest) at compile time, from what it can read:

1. a `StatusCode` written as a **literal** (`=> 422`, `{ get; } = 422`) on the error or on one of its
   bases **declared in the same project**;
2. otherwise the first type in the error's base chain — the error included — named like a framework HTTP
   error: `record WorksiteClosedError : BusinessRuleError` is documented **422** because its base is;
3. otherwise 400.

⚠️ A base in **another project** is read by name only, never by its syntax: the build sees that project
as a DLL, where a property's value cannot be read, and the IDE — which sees it as source — must write the
same contract. So `record UserGone : Shared.GoneError` with `GoneError.StatusCode => 410` in a referenced
project is documented 400: declare the literal on the error itself (`public override int StatusCode =>
410;`) when its base lives elsewhere. What the endpoint *answers* at run time is always the instance's
`StatusCode`; this is only the documented contract.

## ProblemDetails Response

Errors are automatically converted to RFC 7807 ProblemDetails, and served as `application/problem+json` whatever the status (400, 401, 403, 404, 409, 422, 5xx alike). That is the media type the generated OpenAPI document declares for error responses:

```json
{
  "type": "https://httpstatuses.io/404",
  "title": "Not Found",
  "status": 404,
  "code": "NOT_FOUND",
  "entityType": "User",
  "entityId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

`code` is always there; the rest of the extensions are the error's own properties (`WriteExtensions`).
`title` and `detail` are localized by error code when an `IErrorMessageResolver` is registered.

### Customizing Error Response

The response is built from the error alone — `StatusCode`, `Code`, `Title`, `Description` and the
properties `WriteExtensions` writes. There is no per-error hook for headers or for a hand-built
`ProblemDetails`: to change what the client sees, change the error type.

## Void Endpoints

For operations without a response body:

```csharp
[Endpoint(HttpVerb.Delete, "/users/{id}")]
[HttpStatus(204)]
public partial class DeleteUserEndpoint : VoidEndpoint<NotFoundError>
{
    [FromRoute]
    public Guid Id { get; set; }

    public override async Task<VoidResult<NotFoundError>> HandleAsync(CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(Id, ct);
        if (user is null)
            return NotFoundError.For("User", Id);  // → 404

        await _users.DeleteAsync(Id, ct);
        return VoidResult<NotFoundError>.Success();  // → 204 No Content
    }
}
```

The bare `VoidEndpoint` returns a `VoidResult`, which carries no error at all — use it only for an
operation that cannot fail in a way the client should see.

## Error Mapping

The generated handler matches the result: a success becomes the response body, an error goes through
`error.ToResult(httpContext)` (`Pragmatic.Endpoints.Extensions.ErrorExtensions`). That builds the
ProblemDetails described above — localized through the request's `IErrorMessageResolver`, if one is
registered — and writes it as `application/problem+json` with the error's `StatusCode`, through a typed
JSON context rather than an `object`, so the error path stays AOT-safe.

## Exception Handling

For unexpected exceptions, use ASP.NET Core's exception handling:

```csharp
// Program.cs
app.UseExceptionHandler(error =>
{
    error.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Type = "https://httpstatuses.io/500",
            Title = "Internal Server Error",
            Status = 500,
            Detail = "An unexpected error occurred"
        };

        await context.Response.WriteAsJsonAsync(problem);
    });
});
```

## Validation Errors

Integration with Pragmatic.Validation:

```csharp
[Endpoint(HttpVerb.Post, "/users")]
public partial class CreateUser : DomainAction<UserId, ValidationError>
{
    [Required]
    [Email]
    public required string Email { get; set; }

    [Required]
    [MinLength(2)]
    public required string Name { get; set; }

    // Validation runs automatically before Execute
    public override async Task<Result<UserId, IError>> Execute(CancellationToken ct)
    {
        // If we get here, validation passed
        var user = new User(Email, Name);
        await Users.CreateAsync(user, ct);
        return user.Id;
    }
}
```

Validation errors return **422** — the request was understood, the rules refuse it — with the issues
keyed by camel-cased property path. `errors` carries the message **keys**, stable across languages;
`messages`, aligned one for one, carries them resolved in the caller's language when a resolver knows them:

```json
{
  "type": "https://httpstatuses.io/422",
  "title": "Validation Failed",
  "status": 422,
  "code": "VALIDATION_ERROR",
  "errors": {
    "email": ["validation.email"],
    "name": ["validation.minlength"]
  },
  "messages": {
    "email": ["Invalid email format"],
    "name": ["Must be at least 2 characters"]
  }
}
```

## Best Practices

1. **Use typed errors** - Not generic exceptions
2. **Include context** - Resource type, ID, reason
3. **Map to correct status** - Follow HTTP semantics
4. **Localize messages** - Use Pragmatic.Internationalization
5. **Log errors** - But don't expose internals to clients
6. **Document all error types** - In DomainAction generics
