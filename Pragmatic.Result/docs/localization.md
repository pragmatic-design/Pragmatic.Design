# Error Localization

How to localize error messages in Pragmatic.Result.

## Philosophy: Codes, Not Messages

Pragmatic.Result works with **semantic error codes** (`NOT_FOUND`, `VALIDATION_FAILED`, `INSUFFICIENT_FUNDS`), not human-readable messages. Your business logic never allocates message strings. Translation happens at the serialization boundary, when the error becomes an HTTP response.

```
Internal (code everywhere)          Serialization boundary          External (API response)
─────────────────────────           ──────────────────────          ──────────────────────
NotFoundError { Code="NOT_FOUND" }  → IErrorMessageResolver        → { "code": "NOT_FOUND",
  EntityType = "User"               → Resolve("NOT_FOUND", error)      "detail": "User 42 not found" }
  EntityId = "42"
```

Benefits:
- Zero string allocation on hot paths
- Tests assert on codes, not culture-dependent text
- Centralized translation in one place
- Different frontends can translate differently

## IErrorMessageResolver

The extension point is `IErrorMessageResolver` in `Pragmatic.Result.AspNetCore`:

```csharp
public interface IErrorMessageResolver
{
    // Resolves the localized "detail" message for the error code.
    string? Resolve(string code, object? context = null);

    // Resolves the localized "title". Default implementation returns null.
    string? ResolveTitle(string code, object? context = null) => null;
}
```

- `code` is the error's `Code` property (e.g., `"NOT_FOUND"`)
- `context` is the error instance itself (cast to extract details)
- `Resolve` returning `null` leaves `detail` unset; `ResolveTitle` returning `null` keeps the error's own `Title` (or the RFC default title for the status code)

The default `NullErrorMessageResolver` returns null for everything (no localization); responses carry the code, title, and the error's structured extension fields.

## Example: Simple Dictionary Resolver

The simplest approach (no Resx, no external dependencies):

```csharp
using Pragmatic.Result.Http;

public sealed class DictionaryErrorMessageResolver : IErrorMessageResolver
{
    private static readonly Dictionary<string, string> Messages = new()
    {
        ["NOT_FOUND"] = "{0} with ID {1} was not found",
        ["FORBIDDEN"] = "You don't have permission to access this resource",
        ["CONFLICT"] = "{0} already exists",
        ["VALIDATION_FAILED"] = "One or more validation errors occurred"
    };

    public string? Resolve(string code, object? context = null)
    {
        if (!Messages.TryGetValue(code, out var template))
            return null;

        return context switch
        {
            NotFoundError nf => string.Format(template, nf.EntityType ?? "Resource", nf.EntityId ?? "unknown"),
            ConflictError cf => string.Format(template, cf.EntityType ?? "Resource"),
            _ => template
        };
    }
}
```

Register:

```csharp
builder.Services.AddPragmaticResult<DictionaryErrorMessageResolver>();
```

or equivalently:

```csharp
builder.Services.AddSingleton<IErrorMessageResolver, DictionaryErrorMessageResolver>();
builder.Services.AddPragmaticResult();
```

## Example: IStringLocalizer Resolver

If you use ASP.NET Core's localization with resource files or JSON providers:

```csharp
using Pragmatic.Result.Http;

public sealed class LocalizedErrorMessageResolver(IStringLocalizer<ErrorMessages> localizer)
    : IErrorMessageResolver
{
    public string? Resolve(string code, object? context = null)
    {
        var localized = localizer[code];
        if (localized.ResourceNotFound)
            return null;

        return context switch
        {
            NotFoundError nf => string.Format(localized, nf.EntityType ?? "Resource", nf.EntityId ?? "unknown"),
            ConflictError cf => string.Format(localized, cf.EntityType ?? "Resource"),
            _ => localized.Value
        };
    }
}
```

Register with standard ASP.NET Core localization:

```csharp
builder.Services.AddLocalization(opts => opts.ResourcesPath = "Resources");
builder.Services.AddPragmaticResult<LocalizedErrorMessageResolver>();
app.UseRequestLocalization("en", "it", "de");
```

## How It Flows

`AddPragmaticResult()` registers `DefaultProblemDetailsFactory` as the `IProblemDetailsFactory`. The ASP.NET Core filters (`ResultEndpointFilter` for Minimal APIs, `ResultActionFilter` for MVC) resolve it from DI and call it for every failed Result:

1. Your action returns `NotFoundError.Create("User", 42)` as its failure
2. The filter catches the failure and calls `IProblemDetailsFactory.Create(error)`
3. `DefaultProblemDetailsFactory` sets `status` from `error.StatusCode`, `type` from the status code, and `title` from `error.Title` (falling back to the RFC default title)
4. It calls `IErrorMessageResolver.ResolveTitle("NOT_FOUND", error)`: a non-null return overrides `title`
5. It calls `IErrorMessageResolver.Resolve("NOT_FOUND", error)`: a non-null return becomes `detail`; null leaves `detail` unset
6. It writes `code` as an extension, then calls `Error.WriteExtensions()` to emit the error's custom properties as extensions (and `retryAfter` for transient errors)

```json
{
  "type": "https://httpstatuses.io/404",
  "title": "Not Found",
  "status": 404,
  "detail": "User with ID 42 was not found",
  "code": "NOT_FOUND",
  "entityType": "User",
  "entityId": "42"
}
```

## Localization Keys: Code and MessageKey

Two related mechanisms, from wire identifier to resource key:

**`Code`** (on `IError`) is the semantic identifier: UPPER_SNAKE_CASE, stable across cultures, always present in the response. It is what `IErrorMessageResolver` receives.

**`MessageKey`** (on the `Error` base record) is a resource-lookup key derived from `Code`: lowercase, underscores become dots, prefixed with `error.`, so `NOT_FOUND` → `error.not.found`. It is virtual: built-in errors override it per scenario (e.g., `ConflictError.AlreadyExists(...)` yields `error.conflict.already_exists`). The derivation is cached per code. Use it in a resolver when your resource files are organized by dotted keys:

```csharp
public string? Resolve(string code, object? context = null)
    => context is Error error ? localizer[error.MessageKey].Value : null;
```

Localization is driven entirely by `Code` (via `MessageKey`) and the `IErrorMessageResolver.Resolve(code)` / `ResolveTitle(code)` pair; there is no generated `TitleKey`/`DescriptionKey` property. The source generator's only Result-related output is the `WriteExtensions` override for `partial` `Error`-derived types with custom properties, so those properties flow into ProblemDetails extensions with zero reflection.

## Custom Error Types

For domain-specific errors, the same pattern applies:

```csharp
public sealed partial record InsufficientFundsError : Error
{
    public override string Code => "INSUFFICIENT_FUNDS";
    public override int StatusCode => 422;
    public override string Title => "Insufficient Funds";

    public decimal Available { get; init; }
    public decimal Required { get; init; }
}
```

Declaring it `partial` lets the source generator emit the `WriteExtensions` override (so `available` and `required` appear as ProblemDetails extensions automatically).

In your resolver, add a case:

```csharp
InsufficientFundsError isf => string.Format(
    Resolve("INSUFFICIENT_FUNDS") ?? "Insufficient funds: {0:C} available, {1:C} required",
    isf.Available, isf.Required)
```

## Testing

Tests work with codes, never messages:

```csharp
[Fact]
public async Task GetUser_NotFound_ReturnsCorrectCode()
{
    var result = await service.GetUserAsync(999);

    result.IsFailure.Should().BeTrue();
    result.Error.Code.Should().Be("NOT_FOUND");
    // Don't assert on messages: they're culture-dependent
}
```

## Summary

| What | Where |
|------|-------|
| Error codes | `IError.Code` property on each error type |
| Detail translation | `IErrorMessageResolver.Resolve(code, context)` |
| Title translation | `IErrorMessageResolver.ResolveTitle(code, context)` |
| Resource-key convention | `Error.MessageKey` (derived from `Code`) |
| Default behavior | `NullErrorMessageResolver`: no localization, codes pass through |
| Registration | `services.AddPragmaticResult<YourResolver>()` |
| Trigger | `DefaultProblemDetailsFactory` calls the resolver when the filters build the HTTP response |
