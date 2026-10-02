# Common Mistakes

These are the most common issues developers encounter when using Pragmatic.Identity. Each section shows the wrong approach, the correct approach, and explains why.

---

## 1. Accessing Claims Directly Instead of ICurrentUser

**Wrong:**

```csharp
public class OrderService(IHttpContextAccessor httpContextAccessor)
{
    public async Task CreateOrder(CreateOrderRequest request)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var userId = principal?.FindFirst("sub")?.Value
            ?? principal?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        var roles = principal?.FindAll("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
            .Select(c => c.Value).ToList();

        var tenantId = principal?.FindFirst("tenant_id")?.Value;
    }
}
```

**Runtime result:** Works in HTTP requests, but fails silently in background jobs (no `HttpContext`), returns wrong claim types when the IdP changes, and requires manual null handling everywhere.

**Right:**

```csharp
public class OrderService(ICurrentUser currentUser)
{
    public async Task CreateOrder(CreateOrderRequest request)
    {
        var userId = currentUser.Id;
        var roles = currentUser.Authorization.Roles;
        var tenantId = currentUser.TenantId;
    }
}
```

**Why:** `ICurrentUser` abstracts the claim source. In HTTP requests, `ClaimsPrincipalUserAccessor` reads from `HttpContext.User` and normalizes claim types. In background jobs, `SystemUser.Instance` provides a valid identity without `HttpContext`. Claim type mapping is centralized in `IdentityOptions`, so changing the IdP's claim format is a single configuration change, not a codebase-wide find-and-replace.

---

## 2. Not Configuring IdentityOptions When Using a Non-Standard IdP

**Wrong:**

```csharp
// Auth0 uses "sub" for user ID, but the default IdentityOptions.UserIdClaimType is the XML namespace URI
await PragmaticApp.RunAsync(args, app =>
{
    app.UseAuthentication(auth =>
    {
        auth.AddOpenIdConnect("Auth0", options => { /* ... */ });
    });
    // No IdentityOptions configuration!
});
```

**Runtime result:** `ICurrentUser.Id` returns empty string because `ClaimsPrincipalUserAccessor` looks for the claim type `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier` but Auth0 uses `sub`. The user appears anonymous even though they are authenticated.

**Right:**

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    app.UseAuthentication(auth =>
    {
        auth.AddOpenIdConnect("Auth0", options => { /* ... */ });
    });
});

// In your IStartupStep:
services.AddPragmaticIdentity(opts =>
{
    opts.UserIdClaimType = "sub";
    opts.DisplayNameClaimType = "nickname";         // Auth0-specific
    opts.RoleClaimType = "https://myapp/roles";     // Auth0 custom claim
    opts.PermissionClaimType = "permissions";        // Auth0 RBAC
});
```

**Why:** Different identity providers use different claim types. Auth0 uses `sub`, Entra ID uses the long XML namespace URI, Keycloak uses configurable claim types. `IdentityOptions` maps these to `ICurrentUser` properties. Without this mapping, all identity properties resolve to empty/null despite valid authentication.

---

## 3. Checking IsAuthenticated Instead of PrincipalKind

**Wrong:**

```csharp
public class AuditService(ICurrentUser currentUser)
{
    public void RecordAction(string action)
    {
        if (!currentUser.IsAuthenticated)
            return; // Skip audit for "unauthenticated" -- but SystemUser IS authenticated!

        _logger.LogInformation("User {UserId} performed {Action}", currentUser.Id, action);
    }
}
```

**Runtime result:** `SystemUser.Instance` has `IsAuthenticated = true`, so background jobs are logged as if a human user performed the action. This clutters audit logs and makes it impossible to distinguish human actions from system actions.

**Right:**

```csharp
public class AuditService(ICurrentUser currentUser)
{
    public void RecordAction(string action)
    {
        if (currentUser.Kind == PrincipalKind.System)
            return; // Skip audit for system operations

        if (currentUser.Kind == PrincipalKind.Anonymous)
        {
            _logger.LogWarning("Anonymous action: {Action}", action);
            return;
        }

        _logger.LogInformation("User {UserId} performed {Action}", currentUser.Id, action);
    }
}
```

**Why:** `IsAuthenticated` only tells you if there is an identity present. `PrincipalKind` tells you _what kind_ of identity it is. `SystemUser` is authenticated (it needs to pass authorization checks in the action pipeline), but it represents the application itself, not a human. Use `PrincipalKind` when you need to distinguish humans from machines from the system.

---

## 4. Forgetting to Register HeaderUserMiddleware in the Pipeline

**Wrong:**

```csharp
// Program.cs
await PragmaticApp.RunAsync(args, app =>
{
    app.UseAuthentication<NoOpAuthenticationHandler>("PragmaticDefault");
});

// IStartupStep — middleware NOT registered
public void ConfigurePipeline(IApplicationBuilder app)
{
    app.UseAuthentication();
    app.UseAuthorization();
    // HeaderUserMiddleware is missing!
}
```

Requests with `X-User-Id` header:

```http
GET /api/orders
X-User-Id: user-42
X-User-Name: Jane Doe
```

**Runtime result:** The request arrives at the `NoOpAuthenticationHandler` but `HttpContext.User` has no identity set (the middleware that reads the headers was never registered). `NoOpAuthenticationHandler` returns `AuthenticateResult.NoResult()`. The user is anonymous.

**Right:**

```csharp
public void ConfigurePipeline(IApplicationBuilder app)
{
    app.UseMiddleware<HeaderUserMiddleware>();  // BEFORE UseAuthentication
    app.UseAuthentication();
    app.UseAuthorization();
}
```

**Why:** `HeaderUserMiddleware` creates a `ClaimsPrincipal` from HTTP headers and sets `HttpContext.User`. `NoOpAuthenticationHandler` then trusts that identity. Without the middleware, there is no identity to trust. The middleware must run **before** `UseAuthentication()` because the auth handler reads `HttpContext.User` during the authentication step.

---

## 5. Placing HeaderUserMiddleware After UseAuthentication

**Wrong:**

```csharp
public void ConfigurePipeline(IApplicationBuilder app)
{
    app.UseAuthentication();                          // Runs first -- no identity yet
    app.UseMiddleware<HeaderUserMiddleware>();         // Runs second -- too late!
    app.UseAuthorization();
}
```

**Runtime result:** The authentication middleware runs before `HeaderUserMiddleware` sets the identity. `NoOpAuthenticationHandler` sees no identity and returns `NoResult`. Then `HeaderUserMiddleware` sets the identity, but the authentication step already passed. The user appears unauthenticated to the authorization middleware and all downstream code.

**Right:**

```csharp
public void ConfigurePipeline(IApplicationBuilder app)
{
    app.UseMiddleware<HeaderUserMiddleware>();         // First: set identity
    app.UseAuthentication();                          // Second: authenticate
    app.UseAuthorization();                           // Third: authorize
}
```

**Why:** ASP.NET Core middleware order matters. The authentication middleware calls the authentication handler, which reads `HttpContext.User`. If `HeaderUserMiddleware` has not set the user yet, the handler sees no identity. The order must be: set identity, authenticate, authorize.

---

## 6. Using SystemUser in Request-Scoped Code

**Wrong:**

```csharp
// Registering SystemUser for all scopes -- including HTTP requests
services.AddScoped<ICurrentUser>(_ => SystemUser.Instance);
```

**Runtime result:** Every HTTP request runs as "system" with full access. All permission checks pass, all audit fields record "system", and all tenant filtering is bypassed.

**Right:**

```csharp
// SystemUser only for background job scopes
public class MyBackgroundJob(IServiceScopeFactory scopeFactory)
{
    public async Task Execute()
    {
        await using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<IServiceCollection>(); // Just an example

        // Override ICurrentUser for this scope only
        var services = new ServiceCollection();
        services.AddScoped<ICurrentUser>(_ => SystemUser.Instance);
        // ... or register it when building the job's service scope
    }
}
```

For HTTP requests, let `AddPragmaticIdentity()` register `ClaimsPrincipalUserAccessor`:

```csharp
// In your IStartupStep
services.AddPragmaticIdentity();
```

**Why:** `AddPragmaticIdentity()` registers `ClaimsPrincipalUserAccessor` as a scoped `ICurrentUser`. If you override this with `SystemUser.Instance` globally, every request gets system-level access. Only use `SystemUser` in explicit non-HTTP scopes like background jobs, data migrations, and seed operations.

---

## 7. Not Calling AddPragmaticIdentity When Using Standalone Setup

**Wrong:**

```csharp
// Program.cs — standalone, without Pragmatic.Composition
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer(options => { /* ... */ });
builder.Services.AddAuthorization();

// Missing: builder.Services.AddPragmaticIdentity();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.Run();
```

**Runtime result:** `ICurrentUser` is not registered in DI. Any service that injects `ICurrentUser` gets an `InvalidOperationException` at runtime. The `IPermissionChecker` is also not registered, so `[RequirePermission]` policies fail.

**Right:**

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer(options => { /* ... */ });
builder.Services.AddAuthorization();
builder.Services.AddPragmaticIdentity();  // Registers ICurrentUser, IPermissionChecker, IAuthorizationHandler

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.Run();
```

**Why:** When using `PragmaticApp.RunAsync` with `UseAuthentication()` or `UseJwtAuthentication()`, the identity services are registered automatically. In a standalone setup without the Pragmatic builder extensions, you must call `AddPragmaticIdentity()` explicitly to register `ClaimsPrincipalUserAccessor`, `ClaimsPermissionChecker`, and `PragmaticPermissionHandler`.

---

## 8. JWT SigningKey Too Short

**Wrong:**

```csharp
app.UseJwtAuthentication(jwt =>
{
    jwt.SigningKey = "my-secret";  // 9 characters -- too short!
    jwt.Issuer = "https://myapp.com";
});
```

**Runtime result:** `ArgumentException` at startup: "JWT SigningKey must be configured." If the key passes the null check but is shorter than 32 characters, the `SymmetricSecurityKey` constructor throws: "IDX10603: The algorithm 'HS256' requires a key size of at least '256' bits."

**Right:**

```csharp
app.UseJwtAuthentication(jwt =>
{
    jwt.SigningKey = "your-super-secret-key-that-is-at-least-32-characters-long!";
    jwt.Issuer = "https://myapp.com";
});
```

**Why:** HMAC-SHA256 requires a key of at least 256 bits (32 bytes/characters). Shorter keys are cryptographically weak and rejected by the `Microsoft.IdentityModel.Tokens` library. In production, store the key in a secrets manager, not in `appsettings.json`.

---

## 9. Embedding All Permissions in the JWT Token

**Wrong:**

```csharp
// Generating a token with every permission the user has
var allPermissions = await permissionStore.GetAllPermissionsForUser(userId);  // Could be 200+ strings

var token = jwtGenerator.Generate(
    subject: identityKey,
    roles: roles,
    permissions: allPermissions);  // Token is now 5KB+
```

**Runtime result:** The JWT token grows proportionally with the number of permissions. A user with 200 permissions produces a token that exceeds typical HTTP header size limits (8KB on many servers). Worse, permission changes require the user to re-authenticate to get an updated token.

**Right:**

```csharp
// Embed only roles in the token; resolve permissions at runtime via the provider chain
var token = jwtGenerator.Generate(
    subject: identityKey,
    displayName: profile.Name,
    roles: roles);  // Compact token
```

Then configure `RoleExpansionProvider` with `IRolePermissionStore` (in-memory or EF-backed) to resolve permissions from roles at runtime:

```csharp
app.UseAuthorization(authz =>
{
    authz.MapRole<AdminRole>();
    authz.MapRole<BookingManagerRole>(r => r.WithPermissions("booking.*"));
});
```

**Why:** The permission resolution chain exists specifically to avoid bloating JWT tokens. Embed roles (typically 1-5 per user), then let `RoleExpansionProvider` expand roles into permissions per-request (cached). This keeps tokens small, and permission changes take effect immediately without re-authentication. Use `Identity.Persistence` with `EfRolePermissionStore` for runtime-manageable mappings.

---

## 10. Manually Implementing ICurrentUser Instead of Using the Built-in Implementations

**Wrong:**

```csharp
public class CustomCurrentUser : ICurrentUser
{
    public string Id { get; set; } = "";
    public string? DisplayName { get; set; }
    public bool IsAuthenticated { get; set; }
    public PrincipalKind Kind { get; set; }
    public string? TenantId { get; set; }
    public string? ImpersonatedBy { get; set; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims { get; set; } = new Dictionary<string, IReadOnlyList<string>>();
    public IUserAuthorization Authorization { get; set; } = NullUserAuthorization.Instance;
    public IAuthenticationContext Authentication { get; set; } = NullAuthenticationContext.Instance;
}

services.AddScoped<ICurrentUser>(sp =>
{
    var httpContext = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
    var user = new CustomCurrentUser();
    // Manual claim parsing, normalization, authorization wiring...
    return user;
});
```

**Runtime result:** Works initially, but you lose claim normalization, lazy authorization resolution, the `CachedPermissionResolver` integration, the circular dependency workaround, and the permission provider chain. Every new claim type or authorization feature requires changes to your custom implementation.

**Right:**

```csharp
// Use the built-in implementation
services.AddPragmaticIdentity(opts =>
{
    opts.UserIdClaimType = "sub";  // Customize claim mapping if needed
});
```

For testing, use the built-in implementations:

```csharp
// In tests
services.AddScoped<ICurrentUser>(_ => SystemUser.Instance);
// Or use HeaderUserMiddleware with test headers
```

**Why:** `ClaimsPrincipalUserAccessor` handles claim type normalization, lazy `IUserAuthorization` resolution (breaking the circular dependency with `CachedPermissionResolver`), multi-valued claim aggregation, and `IAuthenticationContext` construction from standard OIDC claims. Reimplementing all of this manually is error-prone and will drift from the framework's behavior as features are added.

---

## 11. Forgetting to Implement ILocalIdentityStore

**Wrong:**

```csharp
// Referenced Pragmatic.Identity.Local, but no ILocalIdentityStore registered
// The domain actions (RegisterUser, LoginUser, etc.) are available but have no store
```

**Runtime result:** `InvalidOperationException` at runtime when `RegisterUser` or `LoginUser` is invoked: "Unable to resolve service for type 'ILocalIdentityStore'". The SG generates invokers for the domain actions, but the persistence contract is not satisfied.

**Right:** give the `[PragmaticUser]` entity a `LocalIdentity` property. With `Pragmatic.Persistence.EFCore`
referenced, the generator writes `{User}.LocalIdentityStore` and registers it:

```csharp
[Entity]
[PragmaticUser(MatchClaim = "sub")]
public partial class AppUser : IEntity
{
    public LocalIdentity? Identity { get; set; }
}
```

**Why:** `Pragmatic.Identity.Local` is storage-agnostic, and the store it needs is derivable from the user
entity — which property holds the credentials, which boundary saves it. A store you write yourself (Dapper,
in-memory for tests) still works: a class implementing `ILocalIdentityStore` in the project, and nothing is
generated. Compare the emails it receives as given: they arrive normalized.

---

## Quick Reference

| Mistake | Symptom |
|---------|---------|
| Accessing `ClaimsPrincipal` directly | Breaks in background jobs, fragile claim type strings |
| Missing `IdentityOptions` config | `ICurrentUser.Id` empty despite valid authentication |
| `IsAuthenticated` instead of `PrincipalKind` | Cannot distinguish human from system |
| Missing `HeaderUserMiddleware` | Dev headers ignored, user always anonymous |
| Wrong middleware order | Headers set after auth runs, user always anonymous |
| `SystemUser` in request scope | All requests get full access |
| Missing `AddPragmaticIdentity()` | `ICurrentUser` not in DI, runtime exceptions |
| JWT key too short | Startup exception, HMAC-SHA256 requires 256 bits |
| Too many permissions in JWT | Token exceeds header limits, stale permissions |
| Custom `ICurrentUser` implementation | Loses claim normalization, permission chain, lazy resolution |
| Missing `ILocalIdentityStore` | Runtime DI exception on login/register |
