---
title: "Architecture and Core Concepts"
description: "This guide explains **why** Pragmatic.Identity exists, how its pieces fit together, and how to choose the right package for each situation. Read this before div"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Identity/docs/concepts.md
sidebar:
  order: 1
---
This guide explains **why** Pragmatic.Identity exists, how its pieces fit together, and how to choose the right package for each situation. Read this before diving into the individual feature guides.

---

## The Problem

ASP.NET Core provides `ClaimsPrincipal` as the identity model. It works, but it pushes significant ceremony and fragility onto every developer who needs to answer basic questions about the current user.

### ClaimsPrincipal: stringly typed and scattered

```csharp
public class OrderService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public OrderService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task CreateOrder(CreateOrderRequest request)
    {
        var principal = _httpContextAccessor.HttpContext?.User;

        // Who is the user? Depends on the IdP's claim type.
        var userId = principal?.FindFirst("sub")?.Value
            ?? principal?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value
            ?? throw new UnauthorizedAccessException("No user ID claim");

        // Is the user authenticated? Nullable chain.
        if (principal?.Identity?.IsAuthenticated != true)
            throw new UnauthorizedAccessException();

        // Does the user have the right permission? Manual claim inspection.
        var permissions = principal.FindAll("permission").Select(c => c.Value);
        if (!permissions.Contains("orders.create"))
            throw new ForbiddenException();

        // Multi-tenancy? Another claim type to remember.
        var tenantId = principal.FindFirst("tenant_id")?.Value;

        // Roles? Different claim URI depending on the IdP.
        var roles = principal.FindAll("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
            .Select(c => c.Value);

        // MFA status? Check the amr claim.
        var isMfa = principal.FindFirst("amr")?.Value?.Contains("mfa") == true;

        // Every service that touches identity repeats this pattern.
    }
}
```

Every service that needs identity context repeats the same pattern: resolve `IHttpContextAccessor`, null-check the `HttpContext`, navigate the `ClaimsPrincipal`, remember the claim type URIs, handle multi-valued claims. The claim type strings vary across identity providers -- `sub` vs the full XML namespace URI, `role` vs the Microsoft claim URI. One mistyped string and the permission check silently fails.

### The real issues

**1. No domain abstraction.** `ClaimsPrincipal` is a transport object, not a domain concept. Your business logic should not know about claim types, authentication schemes, or HTTP contexts.

**2. Scattered claim parsing.** Every service that needs the user ID, tenant, or permissions duplicates the same `FindFirst`/`FindAll` logic with the same magic strings.

**3. No separation of concerns.** Authentication metadata (scheme, issuer, MFA status), authorization data (roles, permissions, groups), and identity data (user ID, display name) are all mixed in the same flat claim list.

**4. Environment coupling.** Development requires a real identity provider or manual `ClaimsPrincipal` construction. Switching between dev headers, JWT tokens, and external IdPs requires rewriting authentication plumbing.

**5. No background job story.** Background jobs and data migrations run outside HTTP requests. There is no `HttpContext`, no `ClaimsPrincipal`, but the business logic still needs to know "who" is running the operation for audit fields, permission checks, and event metadata.

---

## The Solution

Pragmatic.Identity replaces the stringly-typed `ClaimsPrincipal` with a structured `ICurrentUser` interface that separates identity, authorization, and authentication into dedicated sub-objects. The same service code works across HTTP requests, background jobs, and integration tests without modification.

The same order creation logic:

```csharp
public class OrderService(ICurrentUser currentUser)
{
    public async Task CreateOrder(CreateOrderRequest request)
    {
        // Type-safe, no magic strings
        if (!currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException();

        var userId = currentUser.Id;                           // Always populated (empty for anonymous)
        var tenantId = currentUser.TenantId;                   // Null when not multi-tenant

        // Authorization: structured, supports wildcard matching
        if (!currentUser.Authorization.HasPermission("orders.create"))
            throw new ForbiddenException();

        if (currentUser.Authorization.IsInRole("admin"))
            /* admin-specific logic */;

        // Authentication metadata: when you need to know *how* the user authenticated
        var isMfa = currentUser.Authentication.IsMfaAuthenticated;
        var issuer = currentUser.Authentication.Issuer;
    }
}
```

The `ICurrentUser` interface is defined in `Pragmatic.Abstractions` (Layer 0) and implemented differently depending on the context:

| Context | Implementation | How Identity Is Populated |
|---------|---------------|--------------------------|
| HTTP request | `ClaimsPrincipalUserAccessor` | Reads claims from `HttpContext.User`, normalizes claim types |
| Background job | `SystemUser.Instance` | Static singleton with full access, `PrincipalKind.System` |
| Unauthenticated | `AnonymousUser.Instance` | Static singleton, all permissions denied |
| Integration test | `HeaderUserMiddleware` | Creates `ClaimsPrincipal` from HTTP headers |

The implementation is invisible to consumers. A `DomainAction`, a repository, or an endpoint all inject `ICurrentUser` and get the right identity for their execution context.

---

## How It Works: The Identity Pipeline

Every HTTP request flows through a pipeline that transforms raw authentication data into the structured `ICurrentUser`:

```
HTTP Request
  |
  v
HeaderUserMiddleware (dev only)        Creates ClaimsPrincipal from X-User-* headers
  |
  v
UseAuthentication()                    ASP.NET Core authenticates (JWT, OIDC, cookie, etc.)
  |
  v
NoOpAuthenticationHandler (dev)        Trusts identity set by HeaderUserMiddleware
  -- or --
JwtBearerHandler (production)          Validates JWT token, populates ClaimsPrincipal
  -- or --
OpenIdConnectHandler (external IdP)    Validates OIDC token
  |
  v
UseAuthorization()                     Evaluates policies ([RequirePermission], [RequirePolicy])
  |                                    Delegates to PragmaticPermissionHandler -> IPermissionChecker
  v
ClaimsPrincipalUserAccessor            Reads HttpContext.User, normalizes claim types
  |                                    Lazy-resolves IUserAuthorization via CachedPermissionResolver
  |
  v
ICurrentUser available                 Inject anywhere in request scope
  |
  +-- .Id                              From UserIdClaimType (default: nameidentifier URI)
  +-- .DisplayName                     From DisplayNameClaimType
  +-- .TenantId                        From TenantClaimType (default: tenant_id)
  +-- .Kind                            User if authenticated, Anonymous if not
  +-- .Claims                          All claims, normalized to short keys
  +-- .Authorization                   CachedPermissionResolver (roles, permissions, groups)
  +-- .Authentication                  ClaimsAuthenticationContext (scheme, issuer, MFA, expiry)
```

The pipeline steps that apply depend on your configuration. A development setup uses `HeaderUserMiddleware` + `NoOpAuthenticationHandler`. A production setup uses `JwtBearerHandler` or an OIDC handler. The `ClaimsPrincipalUserAccessor` normalizes both into the same `ICurrentUser` shape.

---

## ICurrentUser: The Central Abstraction

`ICurrentUser` is defined in `Pragmatic.Abstractions` so that every layer -- from persistence to actions to endpoints -- can depend on it without pulling in ASP.NET Core.

```csharp
public interface ICurrentUser
{
    string Id { get; }                          // User identifier (empty for anonymous)
    string? DisplayName { get; }                // Human-readable name
    bool IsAuthenticated { get; }               // Whether authenticated
    PrincipalKind Kind { get; }                 // Anonymous | User | Service | System
    string? TenantId { get; }                   // Multi-tenant identifier
    string? ImpersonatedBy { get; }             // Impersonator user ID (if applicable)

    IReadOnlyDictionary<string, IReadOnlyList<string>> Claims { get; }  // All claims, multi-valued

    IUserAuthorization Authorization { get; }   // Roles, permissions, groups, scopes
    IAuthenticationContext Authentication { get; } // Scheme, issuer, MFA, expiration
}
```

### Property Composition

The interface uses **property composition** to separate three distinct concerns into dedicated sub-objects. This prevents the "god object" anti-pattern where a single interface grows to 30+ members as new auth features are added.

```
ICurrentUser
  |
  +-- Core identity: Id, DisplayName, IsAuthenticated, Kind, TenantId, ImpersonatedBy
  |
  +-- .Authorization (IUserAuthorization)
  |     Roles, Permissions, Groups, Scopes
  |     HasPermission(), HasAnyPermission(), HasAllPermissions()
  |     IsInRole(), IsInGroup(), HasScope()
  |
  +-- .Authentication (IAuthenticationContext)
  |     Scheme, Protocol, Issuer, Subject
  |     IsMfaAuthenticated, AuthenticatedAt, ExpiresAt
  |     ExternalIdentityKey ("{issuer}|{subject}")
  |
  +-- .Claims (IReadOnlyDictionary<string, IReadOnlyList<string>>)
        All raw claims with normalized keys
        Multi-valued (one claim type can have multiple values)
```

Most code only needs the core properties (`Id`, `IsAuthenticated`, `TenantId`). Authorization checks use `.Authorization`. Authentication metadata is only accessed when the application needs to know _how_ the user authenticated -- for MFA step-up, token refresh decisions, or audit logging.

---

## PrincipalKind

The `PrincipalKind` enum identifies what type of caller is making the current request. This is essential for code that needs to behave differently for humans, services, and system operations.

```csharp
public enum PrincipalKind
{
    Anonymous,   // No authenticated identity
    User,        // Human user via identity provider
    Service,     // Machine-to-machine caller (API key, client credentials)
    System       // The application itself (background jobs, migrations)
}
```

### When to Check PrincipalKind

| Scenario | Check |
|----------|-------|
| Skip audit logging for system operations | `currentUser.Kind == PrincipalKind.System` |
| Deny service accounts from user-only endpoints | `currentUser.Kind != PrincipalKind.Service` |
| Apply rate limiting only to external callers | `currentUser.Kind == PrincipalKind.User` |
| Log differently for anonymous vs authenticated | `currentUser.Kind == PrincipalKind.Anonymous` |

### Built-in Implementations by Kind

| Kind | Implementation | Authorization | Use |
|------|---------------|---------------|-----|
| `Anonymous` | `AnonymousUser.Instance` | `NullUserAuthorization` (all denied) | Fallback for unauthenticated requests |
| `User` | `ClaimsPrincipalUserAccessor` | `CachedPermissionResolver` (lazy) | HTTP requests from human users |
| `System` | `SystemUser.Instance` | `FullAccessUserAuthorization` (all granted) | Background jobs, seed, migrations |
| `Service` | Application-defined | Application-defined | API-to-API calls |

`ClaimsPrincipalUserAccessor` returns `PrincipalKind.User` when `IsAuthenticated` is true and `PrincipalKind.Anonymous` when false. The `Service` kind requires application-specific logic to differentiate service tokens from user tokens -- typically by checking a custom claim like `client_id` or `token_type`.

---

## Claims System

`ICurrentUser.Claims` exposes all claims as a `IReadOnlyDictionary<string, IReadOnlyList<string>>`. Each claim type maps to a list of values, supporting multi-valued claims (a user can have multiple roles, permissions, or groups).

### Claim Type Normalization

`ClaimsPrincipalUserAccessor` normalizes long-form claim type URIs to short names. This means your code works the same way regardless of whether claims come from a JWT, OIDC provider, or the `HeaderUserMiddleware`:

| Long Form (from .NET / IdPs) | Normalized Key |
|-----|-----|
| `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier` | `sub` |
| `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name` | `name` |
| `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` | `role` |
| (configured `PermissionClaimType`, default `permission`) | `permission` |
| (configured `TenantClaimType`, default `tenant_id`) | `tenant_id` |

Custom claims that are not in the normalization map pass through unchanged. A JWT claim `department` is accessible as `Claims["department"]`.

### Configuring Claim Types

If your identity provider uses non-standard claim types, configure `IdentityOptions`:

```csharp
services.AddPragmaticIdentity(opts =>
{
    opts.UserIdClaimType = "sub";                    // Default: XML namespace URI
    opts.DisplayNameClaimType = "preferred_username"; // Default: XML namespace URI
    opts.RoleClaimType = "roles";                    // Default: Microsoft role URI
    opts.PermissionClaimType = "permissions";         // Default: "permission"
    opts.TenantClaimType = "org_id";                 // Default: "tenant_id"
});
```

### Accessing Claims Directly

For application-specific claims not covered by `ICurrentUser` properties:

```csharp
public class SomeService(ICurrentUser currentUser)
{
    public string? GetDepartment()
    {
        return currentUser.Claims.TryGetValue("department", out var values)
            ? values.FirstOrDefault()
            : null;
    }

    public IReadOnlyList<string> GetCustomScopes()
    {
        return currentUser.Claims.TryGetValue("scope", out var scopes)
            ? scopes
            : [];
    }
}
```

---

## IUserAuthorization: The Authorization Sub-Object

`IUserAuthorization` is accessed via `ICurrentUser.Authorization`. It provides the runtime authorization context: roles, permissions, groups, and scopes with typed query methods.

```csharp
public interface IUserAuthorization
{
    IReadOnlyCollection<string> Roles { get; }
    IReadOnlySet<string> Permissions { get; }       // Expanded via permission providers
    IReadOnlyCollection<string> Groups { get; }
    IReadOnlyCollection<string> Scopes { get; }

    bool HasPermission(string permission);           // Exact + wildcard match
    bool HasAnyPermission(IEnumerable<string> permissions);  // OR
    bool HasAllPermissions(IEnumerable<string> permissions);  // AND
    bool IsInRole(string role);
    bool IsInGroup(string group);
    bool HasScope(string scope);
}
```

### Permission Resolution Chain

Permissions are not just read from claims. They are **resolved** through a provider chain that can expand roles into permissions and groups into roles. The chain is ordered and extensible:

```
ClaimsPrincipal (HTTP request)
    |
    v
ClaimsPrincipalUserAccessor (ICurrentUser)
    |
    v
CachedPermissionResolver (IUserAuthorization)
    |  Collects from all IPermissionProvider instances, ordered by .Order
    |
    +-- ClaimsPermissionProvider      (Order=0, reads "permission" claims directly)
    +-- RoleExpansionProvider          (Order=100, role -> permissions via IRolePermissionStore)
    +-- GroupExpansionProvider          (Order=200, group -> roles -> permissions via IGroupRoleStore)
    +-- [Custom providers...]
    |
    v
Merged permission set (HashSet<string>, case-insensitive)
    |
    v
WildcardMatcher for pattern checks (e.g., "booking.*" matches "booking.reservation.create")
```

The resolution is:
- **Lazy**: permissions are not resolved until the first call to `HasPermission()`, `.Permissions`, or any authorization method.
- **Cached per request**: once resolved, the same permission set is reused for all checks within the request scope.
- **Optionally cached cross-request**: when configured via `UsePermissionCache()`, resolved permissions are stored in `ICacheStack` and reused across requests for the same user.

### Wildcard Permissions

The `HasPermission()` method supports glob-style wildcard matching via `WildcardMatcher`:

```csharp
// User has permission "booking.*"
currentUser.Authorization.HasPermission("booking.reservation.create");  // true
currentUser.Authorization.HasPermission("booking.guest.read");          // true
currentUser.Authorization.HasPermission("billing.invoice.read");        // false

// User has permission "*" (superadmin)
currentUser.Authorization.HasPermission("anything.at.all");             // true
```

Wildcard matching uses dot-separated segments. The `*` wildcard matches one or more segments at the position where it appears.

---

## IAuthenticationContext: The Authentication Sub-Object

`IAuthenticationContext` is accessed via `ICurrentUser.Authentication`. It provides metadata about _how_ the user authenticated, not _who_ they are or _what_ they can do.

```csharp
public interface IAuthenticationContext
{
    string? Scheme { get; }                // "Bearer", "Cookie"
    string? Protocol { get; }              // "oidc", "saml2", "apikey"
    string? Issuer { get; }                // Token issuer URI
    string? Subject { get; }               // Subject identifier
    bool IsMfaAuthenticated { get; }       // Multi-factor status
    DateTimeOffset? AuthenticatedAt { get; }
    DateTimeOffset? ExpiresAt { get; }
    string? ExternalIdentityKey { get; }   // "{issuer}|{subject}" for IdP correlation
}
```

### Claim Mapping

`ClaimsAuthenticationContext` reads these from standard OIDC/JWT claims:

| Property | Source Claim | Notes |
|----------|-------------|-------|
| `Scheme` | `ClaimsIdentity.AuthenticationType` | Set by the auth handler ("Bearer", "HeaderAuth", etc.) |
| `Protocol` | `acr` (Authentication Context Class Reference) | OIDC `acr` claim |
| `Issuer` | `iss` | Standard JWT claim |
| `Subject` | Configured `UserIdClaimType` | Same source as `ICurrentUser.Id` |
| `IsMfaAuthenticated` | `amr` (Authentication Methods References) | Checks for "mfa" in the `amr` claim value |
| `AuthenticatedAt` | `auth_time` | Unix epoch timestamp |
| `ExpiresAt` | `exp` | Unix epoch timestamp |
| `ExternalIdentityKey` | Computed `"{iss}|{sub}"` | Stable key for correlating an external identity with a local record |

### When to Use Authentication Context

Most application code does not need authentication metadata. Use it when:

- **MFA step-up**: require multi-factor for sensitive operations (`Authentication.IsMfaAuthenticated`)
- **Token refresh**: check if the token is about to expire (`Authentication.ExpiresAt`)
- **Audit logging**: record which IdP authenticated the user (`Authentication.Issuer`)
- **Linking an external identity to a local record**: correlate on `Authentication.ExternalIdentityKey`
  (the framework does not do this for you — there is no JIT provisioning)
- **Protocol-aware logic**: behave differently for OIDC vs API key (`Authentication.Protocol`)

---

## Built-in ICurrentUser Implementations

### AnonymousUser

Defined in `Pragmatic.Abstractions`. Represents unauthenticated requests.

```csharp
AnonymousUser.Instance
    .Id              -> ""  (empty string)
    .IsAuthenticated -> false
    .Kind            -> PrincipalKind.Anonymous
    .Authorization   -> NullUserAuthorization (all checks return false)
    .Authentication  -> NullAuthenticationContext (all properties null/false)
```

All permission checks return `false`. All role and group checks return `false`. This is the safe default -- an anonymous user has no access unless explicitly granted via `[AllowAnonymous]`.

### SystemUser

Defined in `Pragmatic.Identity`. Represents the application itself during background operations.

```csharp
SystemUser.Instance
    .Id              -> "system"
    .IsAuthenticated -> true
    .Kind            -> PrincipalKind.System
    .Authorization   -> FullAccessUserAuthorization (all checks return true)
    .Authentication  -> NullAuthenticationContext
```

All permission checks return `true`. Use this for background jobs, data migrations, seed operations, and event handlers that run outside a user request:

```csharp
// In a background job's DI scope
services.AddScoped<ICurrentUser>(_ => SystemUser.Instance);
```

### ClaimsPrincipalUserAccessor

Defined in `Pragmatic.Identity.AspNetCore`. The standard HTTP implementation.

Reads from `HttpContext.User` and normalizes claims via `IdentityOptions`. Lazy-resolves `IUserAuthorization` to break the circular dependency with `CachedPermissionResolver`.

This is registered as scoped by `AddPragmaticIdentity()` and is the default for all HTTP requests.

---

## The Package Architecture

Pragmatic.Identity is split into five core packages organized in layers, plus three integration packages — OIDC, Keycloak and security auditing — described in [Packages](/modules/identity/packages/#integration-packages). Each layer adds capabilities without requiring the layers above it.

```
Layer 0 (Abstractions)     Layer 1 (Core)          Layer 2 (ASP.NET)       Layer 3 (Features)
Pragmatic.Abstractions --> Pragmatic.Identity  --> Identity.AspNetCore --> Identity.Local.Jwt
  ICurrentUser              SystemUser              ClaimsPrincipalUser     JwtTokenGenerator
  IUserAuthorization        IdentityOptions         HeaderUserMiddleware    UseJwtAuthentication
  IAuthenticationContext    ClaimsPermissionChecker  NoOpAuthHandler
  PrincipalKind             IdentityRecord          PragmaticPermHandler
  AnonymousUser

                                                                           Identity.Local
                                                                             RegisterUser
                                                                             LoginUser
                                                                             SignInUser
                                                                             BcryptHasher

Pragmatic.Authorization ------------------------------------------> Identity.Persistence
  CachedPermissionResolver                                            EfRolePermissionStore
  IPermissionProvider                                                 EfGroupRoleStore
  IRolePermissionStore
```

### Package Selection Decision Tree

| Question | If Yes | If No |
|----------|--------|-------|
| Do you only need `ICurrentUser` in a library (no ASP.NET)? | `Pragmatic.Abstractions` only | Keep reading |
| Need `SystemUser` for background jobs? | `Pragmatic.Identity` | Keep reading |
| Building an ASP.NET Core web app? | `Pragmatic.Identity.AspNetCore` | Keep reading |
| Want self-hosted login/registration? | `Pragmatic.Identity.Local` | Keep reading |
| Want JWT token generation + validation? | `Pragmatic.Identity.Local.Jwt` (pulls in everything) | Keep reading |
| Need database-backed role/permission management? | Add `Pragmatic.Identity.Persistence` | Done |

### Pragmatic.Identity (Core)

No ASP.NET Core dependency, no EF Core dependency. Contains:

| Type | Purpose |
|------|---------|
| `SystemUser` | Singleton `ICurrentUser` for background jobs with `FullAccessUserAuthorization` |
| `IdentityOptions` | Configurable claim type mapping (user ID, name, role, permission, tenant) |
| `IdentityRecord` | Abstract base for identity provider records (local, external) |
| `ClaimsPermissionChecker` | Bridges `ICurrentUser.Authorization` to the async `IPermissionChecker` interface |
| `ProvisionSource` | Enum: `Manual`, `Jit`, `Scim` |

Reference this when you need identity concepts in a non-web project, or when you need `SystemUser` for background job contexts.

### Pragmatic.Identity.AspNetCore

The ASP.NET Core bridge. Maps `HttpContext.User` to `ICurrentUser`.

| Type | Purpose |
|------|---------|
| `ClaimsPrincipalUserAccessor` | Scoped `ICurrentUser` that reads from `HttpContext.User` with claim normalization |
| `ClaimsAuthenticationContext` | Reads `IAuthenticationContext` from standard claims (`iss`, `sub`, `acr`, `amr`, `exp`) |
| `HeaderUserMiddleware` | Development middleware: creates `ClaimsPrincipal` from HTTP headers |
| `NoOpAuthenticationHandler` | Development auth handler: trusts identities from earlier middleware |
| `PragmaticPermissionHandler` | `AuthorizationHandler` delegating to `IPermissionChecker` |

⚠️ `PragmaticPermissionRequirement` and `PermissionMode` — what the handler above evaluates — are
**not** in this package: they ship with `Pragmatic.Endpoints.AspNetCore`, so a boundary declaring an
endpoint puts the requirement in its policy without referencing anything of Identity's.

**DI registration**: `services.AddPragmaticIdentity()` registers `ICurrentUser`, `IPermissionChecker`, and `IAuthorizationHandler`.

**IPragmaticBuilder extensions**:
- `app.UseAuthentication<THandler>(schemeName)` -- configure with a specific handler
- `app.UseAuthentication(auth => ...)` -- full control via `AuthenticationBuilder`

Both extensions automatically call `AddAuthorization()` and `AddPragmaticAuthorization()`.

### Pragmatic.Identity.Local

Self-hosted identity provider with domain actions for credential management:

| Action | Return | Errors |
|--------|--------|--------|
| `RegisterUser` | `ExternalIdentityKey` (string) | `EmailAlreadyExistsError`, `PasswordPolicyError` |
| `LoginUser` | `LoginResult` (key + timestamp) | `InvalidCredentialsError`, `AccountLockedError`, `IdentityNotActiveError`, `EmailNotVerifiedError` |
| `SignInUser` | `AccessToken` (signed by `IAccessTokenIssuer`; claims from each `IUserClaimsContributor`) | same as `LoginUser` |
| `ChangePassword` | void (requires `LocalIdentityPermissions.ChangePassword`) | `InvalidCredentialsError`, `IdentityNotActiveError`, `PasswordPolicyError` |
| `RequestPasswordReset` | void (token sent via notifier) | (none -- never reveals email existence) |
| `ConfirmPasswordReset` | void | `InvalidResetTokenError`, `PasswordPolicyError` |
| `RequestEmailVerification` | void (token sent via notifier) | (none -- never reveals email existence/state) |
| `ConfirmEmail` | void | `InvalidEmailVerificationTokenError` |

The package defines `ILocalIdentityStore` which you must implement (typically via EF Core). Default implementations are provided for `IPasswordHasher` (BCrypt), `ISecurityTokenService` (HMAC-SHA256), and `IPasswordPolicy` (minimum length).

**Token delivery is out-of-band.** `RequestPasswordReset` and `RequestEmailVerification` never return the
token; they hand the plaintext to `IPasswordResetNotifier` / `IEmailVerificationNotifier`. The registered
defaults (`LogOnly*`) intentionally **do not deliver** — they log a warning so the feature fails loud.
Register a real email/SMS-backed notifier in production, or reset/verification links never reach users.

Security features: BCrypt adaptive hashing, account lockout, reset/verification token hashing, timing-safe comparison with equal-cost not-found paths, email existence hiding, security-stamp rotation on password change, and optional `RequireEmailVerification` login gating.

`LocalIdentityPackage` implements `IPackageDefinition` with route prefix `identity/local`.

### Pragmatic.Identity.Local.Jwt

JWT token generation and validation. This is the "top" package -- referencing it brings in the entire stack.

| Type | Purpose |
|------|---------|
| `JwtTokenGenerator` | Creates signed JWT tokens with configurable claims |
| `JwtOptions` | `SigningKey` (required, 256+ bits), `Issuer`, `Audience`, `TokenExpiration`, `ClockSkew` |
| `IAccessTokenIssuer` (from `Identity.Local`) | Implemented by `JwtTokenGenerator` and registered by `UseJwtAuthentication`: it signs `SignInUser`'s `AccessToken` (`Token` + `ExpiresAt`) |

**IPragmaticBuilder extension**: `app.UseJwtAuthentication(jwt => ...)` configures token generation, token validation (JwtBearer handler), `AddAuthorization()`, and `AddPragmaticAuthorization()` in a single call.

### Pragmatic.Identity.Persistence

Database-backed authorization with EF Core. Replaces in-memory stores from `Pragmatic.Authorization` with temporal EF stores.

> There is no user base class or identity `DbContext` to inherit. Use `[UsePackage<LocalIdentityPackage>]`
> with a `[PragmaticUser]` entity; the EF stores and temporal entities below work alongside it. See
> [Persistence](/modules/identity/persistence/).

| Type | Purpose |
|------|---------|
| `EfRolePermissionStore` | `IRolePermissionStore` backed by `RolePermission` table with temporal filtering |
| `EfGroupRoleStore` | `IGroupRoleStore` backed by `GroupRole` table with temporal filtering |

| `IdentityPersistenceBuilder` | Fluent builder: `SkipRolePermissionStore()`, `SkipGroupRoleStore()` |

All authorization entities use `ValidFrom`/`ValidTo` for temporal validity. Temporal queries use `IClock.UtcNow`, making them testable with `FakeClock`.

---

## Development vs Production Authentication

Pragmatic.Identity is designed for config-driven switching between development and production authentication. The same codebase supports both without conditional compilation.

### Development: Header-Based Auth

```csharp
app.UseAuthentication<NoOpAuthenticationHandler>("PragmaticDefault");
```

Requests authenticate via HTTP headers:

```http
GET /api/reservations
X-User-Id: user-42
X-User-Name: Jane Doe
X-User-Roles: admin,booking-manager
X-User-Permissions: reservation.create,reservation.read
X-User-Tenant: tenant-1
X-User-Groups: customer-care
```

`HeaderUserMiddleware` creates a `ClaimsPrincipal` from these headers. `NoOpAuthenticationHandler` trusts whatever identity is present. No identity provider, no tokens, no external dependencies. You control the exact identity with each request.

### Production: JWT Authentication

```csharp
app.UseJwtAuthentication(jwt =>
{
    jwt.SigningKey = app.Configuration["Jwt:Key"]!;
    jwt.Issuer = "https://myapp.example.com";
    jwt.TokenExpiration = TimeSpan.FromHours(1);
});
```

Requests authenticate via Bearer tokens. The JwtBearer handler validates the token and populates `ClaimsPrincipal`. `ClaimsPrincipalUserAccessor` maps the claims to `ICurrentUser`.

### Config-Driven Pattern (Recommended)

```csharp
await PragmaticApp.RunAsync(args, app =>
{
    var jwtKey = app.Configuration["Jwt:Key"];
    if (!string.IsNullOrEmpty(jwtKey))
    {
        app.UseJwtAuthentication(jwt =>
        {
            jwt.SigningKey = jwtKey;
            jwt.Issuer = app.Configuration["Jwt:Issuer"];
        });
    }
    else
    {
        app.UseAuthentication<NoOpAuthenticationHandler>("PragmaticDefault");
    }
});
```

When `Jwt:Key` is present in configuration (production), JWT authentication is used. When absent (development), header-based auth is used. The rest of the application -- domain actions, endpoints, services -- does not change.

### External Identity Provider

For Keycloak, Auth0, Entra ID, or any OIDC provider:

```csharp
app.UseAuthentication(auth =>
{
    auth.AddOpenIdConnect("oidc", options =>
    {
        options.Authority = "https://your-idp.example.com";
        options.ClientId = "your-client-id";
        options.ClientSecret = "your-client-secret";
    });
});
```

The `UseAuthentication()` overload gives you full control over the ASP.NET Core `AuthenticationBuilder` while still wiring up `AddAuthorization()` and `AddPragmaticAuthorization()`.

---

## ASP.NET Core Authorization Bridge

Pragmatic.Identity bridges the Pragmatic permission model with ASP.NET Core's authorization middleware. This happens automatically when you use `UseAuthentication()` or `UseJwtAuthentication()`.

### How It Works

1. The source generator emits `[RequirePermission("booking.reservation.create")]` on endpoints as ASP.NET Core authorization policies with `PragmaticPermissionRequirement`.

2. ASP.NET Core's `UseAuthorization()` middleware evaluates the policy.

3. `PragmaticPermissionHandler` receives the requirement, delegates to `IPermissionChecker`.

4. `IPermissionChecker` (default: `ClaimsPermissionChecker`) calls `ICurrentUser.Authorization.HasPermission()` or `HasAllPermissions()`.

5. The permission check flows through the `CachedPermissionResolver` provider chain.

```
[RequirePermission("booking.reservation.create")]
    |
    v
ASP.NET Core authorization middleware
    |
    v
PragmaticPermissionHandler
    |  Checks: Is user authenticated?
    |  Delegates to IPermissionChecker
    v
ClaimsPermissionChecker
    |  Wraps ICurrentUser.Authorization as async
    v
CachedPermissionResolver.HasPermission()
    |  Resolves via provider chain (Claims -> Roles -> Groups)
    |  Checks WildcardMatcher
    v
true/false -> 200 or 403
```

### PermissionMode

`PragmaticPermissionRequirement` supports two modes:

| Mode | Behavior | Generated From |
|------|----------|---------------|
| `PermissionMode.All` | All listed permissions must be present (AND) | `[RequirePermission("a", "b")]` |
| `PermissionMode.Any` | At least one permission must be present (OR) | `[RequireAnyPermission("a", "b")]` |

---

## Ecosystem Integration

### Persistence: Audit Fields

`Pragmatic.Persistence.EFCore`'s auditing interceptor reads `ICurrentUser.Id` to populate `CreatedBy` and `UpdatedBy` fields on entities implementing `IAuditable`. When the code runs in a background job with `SystemUser.Instance`, the audit field records `"system"`.

### Actions: Authorization Filter

`Pragmatic.Actions` uses `ICurrentUser.Authorization` through the `AuthorizationFilter` in the action pipeline. When a domain action has `[RequirePermission("orders.create")]`, the filter checks the permission before `Execute()` runs.

### Endpoints: [RequirePermission]

`Pragmatic.Endpoints` generates ASP.NET Core authorization policies from `[RequirePermission]` attributes. The SG produces code that creates `PragmaticPermissionRequirement` instances and registers them as endpoint metadata. The `PragmaticPermissionHandler` evaluates them at runtime.

### Multi-Tenancy

`ICurrentUser.TenantId` is consumed by `Pragmatic.MultiTenancy` for tenant-scoped data filtering. The tenant ID flows from the JWT claim or the `X-User-Tenant` header, through `ICurrentUser`, to the persistence layer's tenant filter.

### Caching

The `CachedPermissionResolver` integrates with `Pragmatic.Caching`'s `ICacheStack` for cross-request permission caching. Configure via `UsePermissionCache()` in the authorization builder:

```csharp
app.UseAuthorization(authz =>
{
    authz.UsePermissionCache(TimeSpan.FromMinutes(5));
});
```

Cache key: `permissions:t:{tenantId}:u:{userId}` (multi-tenant) or `permissions:g:u:{userId}` (global).
Tags: `user:{userId}`, `tenant:{tenantId}`, `role:{roleName}`, `group:{groupName}`. Invalidate on demand
via `IPermissionCacheInvalidator` (see the [Authorization docs](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/Pragmatic.Authorization/docs/permission-resolution.md#cache-invalidation)).

---

## See Also

- [Getting Started](/modules/identity/getting-started/) -- Step-by-step setup from zero to working auth
- [Package Architecture](/modules/identity/packages/) -- Detailed description of each sub-package
- [Dev Authentication](/modules/identity/dev-authentication/) -- HeaderUserMiddleware, header format, testing
- [JWT Authentication](/modules/identity/jwt-authentication/) -- JWT setup, token claims, configuration
- [Persistence](/modules/identity/persistence/) -- EF stores, temporal authorization
- [Common Mistakes](/modules/identity/common-mistakes/) -- Wrong/Right/Why format for common pitfalls
- [Troubleshooting](/modules/identity/troubleshooting/) -- Problem/solution guide with diagnostics reference
- [Pragmatic.Authorization docs](/modules/authorization/overview/) -- Role/permission infrastructure, policies, resource authorization
- [Pragmatic.Endpoints docs](/modules/endpoints/overview/) -- How `[RequirePermission]` generates authorization policies
