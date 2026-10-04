---
title: "Troubleshooting"
description: "Practical problem/solution guide for Pragmatic.Identity. Each section covers a common issue, the likely causes, and the fix."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Identity/docs/troubleshooting.md
sidebar:
  order: 8
---
Practical problem/solution guide for Pragmatic.Identity. Each section covers a common issue, the likely causes, and the fix.

---

## The Host Refuses to Start: No Authentication Method for the Environment

```
The endpoints require authorization and no authentication method is configured for environment Production …
```

The endpoints require authorization by default, and nothing can authenticate a caller in the environment
the host is running in: typically `UseDevelopmentIdentity()` guarded by `IsDevelopment()`, and the host
started without a launch profile, so in Production. The host stops there (with the maintenance page,
503, unless `EnableOnStartupFailure` is off) rather than answering 500 to every protected request.

- Run it in the environment you configured (`Properties/launchSettings.json`, or `ASPNETCORE_ENVIRONMENT`).
- Or give that environment a scheme: `UseJwtAuthentication()`, `UseKeycloakAuthentication()`,
  `UseOidcAuthentication()`, `UseAuthentication<THandler>(scheme)`.
- Or, for an application that deliberately has no users, declare the host `[AnonymousHost]`.

---

## ICurrentUser.Id Is Empty Despite Valid Authentication

The user authenticates successfully (no 401), but `ICurrentUser.Id` returns an empty string.

### Checklist

1. **Check the claim type mapping.** `ClaimsPrincipalUserAccessor` reads the user ID from the claim type specified by `IdentityOptions.UserIdClaimType`. The default is the long XML namespace URI `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`. If your IdP uses `sub` or a custom claim:

   ```csharp
   services.AddPragmaticIdentity(opts =>
   {
       opts.UserIdClaimType = "sub";
   });
   ```

2. **Inspect the actual claims.** Add a debug endpoint or log statement to see what claims the token contains:

   ```csharp
   var claims = currentUser.Claims;
   foreach (var (key, values) in claims)
       logger.LogDebug("Claim {Key}: {Values}", key, string.Join(", ", values));
   ```

3. **Check JWT handler claim mapping.** The .NET JWT handler renames `sub` and `role` to the WS-* URIs `IdentityOptions` defaults to, and leaves `name` as it is. `UseJwtAuthentication()` therefore sets `NameClaimType = "name"`, `RoleClaimType = ClaimTypes.Role`, and `IdentityOptions.DisplayNameClaimType = "name"`. If you are using a custom JWT setup, check that `TokenValidationParameters.NameClaimType` and `IdentityOptions.DisplayNameClaimType` name the same claim; when they do not, `User.Identity.Name` has a value and `ICurrentUser.DisplayName` is null.

4. **Check if `AddPragmaticIdentity()` was called.** Without it, `ICurrentUser` is not registered and may resolve to a default or throw.

---

## User Is Always Anonymous (Header Auth Not Working)

Requests with `X-User-Id` headers result in `ICurrentUser.IsAuthenticated = false`.

### Checklist

1. **Is `HeaderUserMiddleware` registered in the pipeline?** It must be added manually in `IStartupStep.ConfigurePipeline`:

   ```csharp
   app.UseMiddleware<HeaderUserMiddleware>();
   ```

2. **Is the middleware order correct?** `HeaderUserMiddleware` must run **before** `UseAuthentication()`:

   ```csharp
   app.UseMiddleware<HeaderUserMiddleware>();  // First
   app.UseAuthentication();                    // Second
   app.UseAuthorization();                     // Third
   ```

3. **Is `NoOpAuthenticationHandler` configured?** The handler trusts identities set by earlier middleware. Without it, ASP.NET Core has no scheme to authenticate with:

   ```csharp
   app.UseAuthentication<NoOpAuthenticationHandler>("PragmaticDefault");
   ```

4. **Is the `X-User-Id` header present and non-empty?** `HeaderUserMiddleware` only creates an identity when `X-User-Id` exists and is not empty. Check for typos in the header name (case-sensitive).

---

## Permissions Always Denied (403 on Everything)

Every request that requires permissions returns 403 Forbidden, even for admin users.

### Checklist

1. **Are role-to-permission mappings configured?** If the user has roles but no direct permissions in claims, `RoleExpansionProvider` must map roles to permissions. Configure via `UseAuthorization()`:

   ```csharp
   app.UseAuthorization(authz =>
   {
       authz.MapRole<AdminRole>();
       authz.MapRole<BookingManagerRole>(r => r
           .WithPermissions("booking.reservation.create", "booking.guest.read"));
   });
   ```

2. **Are roles in the token/headers?** Check that the user's JWT or headers include role claims. For headers:

   ```http
   X-User-Roles: admin,booking-manager
   ```

3. **Is `AddPragmaticAuthorization()` called?** This registers `PragmaticPermissionHandler`. It is called automatically by `UseAuthentication()` and `UseJwtAuthentication()`. In a standalone setup, call it explicitly:

   ```csharp
   services.AddPragmaticAuthorization();
   ```

4. **Is the permission name correct?** Permission checks are case-insensitive but must match the configured permission string exactly (or match a wildcard pattern). A user with `booking.reservation.create` does not match `booking.reservations.create` (plural).

5. **Is the `IPermissionChecker` registered?** The default is `ClaimsPermissionChecker`. Verify it is in DI:

   ```csharp
   var checker = serviceProvider.GetService<IPermissionChecker>();
   // Should not be null
   ```

6. **Is the permission provider chain running?** `CachedPermissionResolver` collects from all `IPermissionProvider` instances. If no providers are registered, the permission set is empty.

---

## JWT Token Rejected (401 Unauthorized)

Requests with a Bearer token return 401.

### Checklist

1. **Is the signing key correct?** The key used for generation must match the key used for validation. If they differ (e.g., different environments), the token signature is invalid.

2. **Is the token expired?** Check the `exp` claim. The default expiration is 1 hour with 1 minute clock skew tolerance.

3. **Is the issuer correct?** If `JwtOptions.Issuer` is set, the token's `iss` claim must match exactly.

4. **Is the audience correct?** If `JwtOptions.Audience` is set, the token's `aud` claim must match exactly.

5. **Is the token well-formed?** Decode the token at [jwt.io](https://jwt.io) and verify:
   - The header has `"alg": "HS256"`
   - The payload has `sub`, `exp`, and `iss` claims
   - The signature is valid with your key

6. **Is the Authorization header format correct?** Must be `Authorization: Bearer <token>` (note the space after "Bearer").

7. **Is `UseJwtAuthentication()` called?** Verify in `Program.cs` that the JWT setup runs:

   ```csharp
   app.UseJwtAuthentication(jwt =>
   {
       jwt.SigningKey = configuration["Jwt:Key"]!;
   });
   ```

---

## ClaimsPrincipalUserAccessor Circular Dependency

At startup, you see an error about circular dependency involving `ICurrentUser` and `IUserAuthorization`.

### Explanation

`CachedPermissionResolver` (which implements `IUserAuthorization`) depends on `ICurrentUser` to read claims. `ClaimsPrincipalUserAccessor` (which implements `ICurrentUser`) depends on `IUserAuthorization` via the `.Authorization` property.

### Solution

This is handled internally. `ClaimsPrincipalUserAccessor` resolves `IUserAuthorization` lazily via `IServiceProvider` instead of constructor injection:

```csharp
public IUserAuthorization Authorization =>
    _authorization ??= serviceProvider.GetRequiredService<IUserAuthorization>();
```

If you see a circular dependency error, it means something else in your DI registration is creating the cycle. Check for services that inject both `ICurrentUser` and `IUserAuthorization` in their constructors and trigger resolution of both during construction.

---

## Permission Cache Not Invalidating

Permissions change in the database, but the user still sees the old permissions.

### Checklist

1. **Is cross-request caching enabled?** Without `UsePermissionCache()`, permissions are resolved fresh per request. If caching is enabled, check the expiration:

   ```csharp
   app.UseAuthorization(authz =>
   {
       authz.UsePermissionCache(TimeSpan.FromMinutes(5));
   });
   ```

2. **Is the cache being invalidated?** When role/permission mappings change, invalidate the cache by user tag:

   ```csharp
   await cache.RemoveByTagAsync($"user:{userId}");
   ```

3. **Is the cache provider registered?** Cross-request caching requires `ICacheStack` from `Pragmatic.Caching`. Without it, `CachedPermissionResolver` falls back to per-request resolution only (no cross-request caching).

4. **Per-request caching is always on.** Even without `UsePermissionCache()`, `CachedPermissionResolver` caches the resolved permission set for the lifetime of the request scope. This means permission changes take effect on the next request.

---

## ILocalIdentityStore Not Found

`RegisterUser` or `LoginUser` throws: "Unable to resolve service for type 'ILocalIdentityStore'".

### Checklist

1. **Does the `[PragmaticUser]` entity have a `LocalIdentity` property?** That is what makes the generator
   write `{User}.LocalIdentityStore`; without one there are no local credentials to store.

2. **Is `Pragmatic.Persistence.EFCore` referenced by the project that declares the user?** The store saves
   through the boundary's unit of work, which that package provides.

3. **Did you write your own?** A class implementing `ILocalIdentityStore` in the project turns the generated
   one off; check it is registered (`[Service]`) and its dependencies resolve.

## Self-registration throws NotSupportedException

`RegisterUser` fails with "… is not created by self-registration". The user entity does not implement
`ISelfRegisteringUser<TUser>`, so the generated store will not guess how to create a user. Implement it
(`static TUser Register(LocalIdentity identity)`) or provision users another way.

---

## Account Lockout Not Working

Failed login attempts do not trigger lockout.

### Checklist

1. **Check `LocalIdentityOptions.MaxFailedLoginAttempts`.** Default is 5. Verify the threshold:

   ```csharp
   services.Configure<LocalIdentityOptions>(opts =>
   {
       opts.MaxFailedLoginAttempts = 5;
       opts.LockoutDuration = TimeSpan.FromMinutes(15);
   });
   ```

2. **Is `ILocalIdentityStore.UpdateAsync` persisting changes?** The `LoginUser` action increments `FailedLoginAttempts` and calls `UpdateAsync`. If `UpdateAsync` does not call `SaveChangesAsync`, the count is lost.

3. **Is `IClock` registered?** Lockout uses `IClock.UtcNow` for timestamp comparison. Without `IClock`, the temporal check fails. This is auto-registered when `Pragmatic.Temporal` is referenced.

---

## Diagnostics Reference

Identity uses the PRAG1000-1099 range shared with Authorization. The following diagnostics are relevant to Identity configuration:

| ID | Severity | Cause | Fix |
|----|----------|-------|-----|
| PRAG1001 | Error | A permission value declared twice, or equal to a CRUD permission | Declare each `[assembly: Permission]` value once |
| PRAG1003 | Error | `IRole.Name` is empty | Ensure the static abstract `Name` property returns a non-empty string literal |
| PRAG1050 | Error | Duplicate `[UsePackage<T>]` on module | Only one `[UsePackage<T>]` per package type per module |
| PRAG1695 | Error | `Pragmatic.Authorization` referenced without `Pragmatic.Identity` in a host that does not declare `[AnonymousHost]` | Reference `Pragmatic.Identity.AspNetCore`, or put `[AnonymousHost]` on the host's `[Module]` class if the host deliberately has no authentication |
| PRAG1696 | Warning | `Pragmatic.Identity.Persistence` referenced without `Pragmatic.Authorization` | Add the `Pragmatic.Authorization` reference |

Permission naming (`boundary.entity.operation`) is a convention, not a compile-time rule: nothing
checks it. It matters because wildcard grants such as `booking.*` match on those segments.

---

## FAQ

### Can I use ICurrentUser outside of HTTP requests?

Yes. In background jobs, register `SystemUser.Instance` as the scoped `ICurrentUser`:

```csharp
services.AddScoped<ICurrentUser>(_ => SystemUser.Instance);
```

In unit tests, use `AnonymousUser.Instance` or create a test double.

### How do I test with specific permissions?

Use `HeaderUserMiddleware` with the `X-User-Permissions` header:

```http
GET /api/orders
X-User-Id: test-user
X-User-Permissions: orders.read,orders.create
```

Or in integration tests with `WebApplicationFactory`, set headers on the `HttpClient`:

```csharp
client.DefaultRequestHeaders.Add("X-User-Id", "test-user");
client.DefaultRequestHeaders.Add("X-User-Permissions", "orders.read,orders.create");
```

### Can I use multiple identity providers simultaneously?

Yes. Use the `UseAuthentication(auth => ...)` overload to configure multiple schemes:

```csharp
app.UseAuthentication(auth =>
{
    auth.AddJwtBearer("Bearer", options => { /* ... */ });
    auth.AddOpenIdConnect("oidc", options => { /* ... */ });
});
```

ASP.NET Core evaluates schemes in order. `ClaimsPrincipalUserAccessor` works with whatever `ClaimsPrincipal` the winning scheme produces.

### Why does ICurrentUser.Kind only return User or Anonymous?

`ClaimsPrincipalUserAccessor` returns `PrincipalKind.User` when `IsAuthenticated` is true, and `PrincipalKind.Anonymous` when false. The `Service` and `System` kinds are set by application-specific implementations. If you need to distinguish service tokens from user tokens, create a custom `ICurrentUser` wrapper that checks a claim (e.g., `client_id` or `token_type`) and returns `PrincipalKind.Service`.

### How do I handle token refresh?

`Pragmatic.Identity.Local.Jwt` generates tokens but does not include a refresh token mechanism. For refresh tokens, implement a refresh endpoint that validates the refresh token (stored in your database) and generates a new JWT via `JwtTokenGenerator.Generate()`. Consider using short-lived access tokens (15 minutes) with longer-lived refresh tokens (7 days).

---

## Getting Help

- **GitHub Issues**: [github.com/pragmatic-design/Pragmatic.Design/issues](https://github.com/pragmatic-design/Pragmatic.Design/issues)
- **Showcase Examples**: See the `Showcase` project for working identity configurations across development and production modes.
- **Package Architecture**: See [packages.md](/modules/identity/packages/) for detailed descriptions of each sub-package.
- **Dev Authentication**: See [dev-authentication.md](/modules/identity/dev-authentication/) for header format and testing patterns.
- **JWT Authentication**: See [jwt-authentication.md](/modules/identity/jwt-authentication/) for token generation and validation details.
- **Authorization**: See [Pragmatic.Authorization docs](/modules/authorization/overview/) for role/permission configuration.
