# Pragmatic.Identity

Identity and authentication for the Pragmatic.Design ecosystem — a structured `ICurrentUser` that works
in HTTP requests, background jobs, and tests, layering up to database-backed identity with temporal
roles and groups.

## The Problem

ASP.NET Core's `ClaimsPrincipal` is a transport object, not a domain abstraction. Every service that
needs the current user repeats the same pattern: resolve `IHttpContextAccessor`, null-check
`HttpContext`, navigate claims with magic-string URIs, and special-case each provider. Background jobs
have no `HttpContext` at all, so the whole pattern fails outside HTTP.

```csharp
// Without Pragmatic.Identity: magic strings, fragile, HTTP-only
var userId = principal?.FindFirst("sub")?.Value
    ?? principal?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
```

## The Solution

Replace `ClaimsPrincipal` with a structured `ICurrentUser` (identity + authorization + authentication
sub-objects). Claim mapping is centralized and configurable; the same code runs across HTTP
(`ClaimsPrincipalUserAccessor`), background jobs (`SystemUser`), and tests — unchanged. Dev vs
production auth switches by configuration (HTTP headers in dev, JWT in production).

```csharp
public class OrderService(ICurrentUser currentUser)
{
    public VoidResult<ForbiddenError> CreateOrder(CreateOrderRequest request)
    {
        var userId   = currentUser.Id;
        var tenantId = currentUser.TenantId;
        if (!currentUser.Authorization.HasPermission("orders.create"))
            return ForbiddenError.MissingPermission("orders.create");
        // ...
        return VoidResult<ForbiddenError>.Success();
    }
}
```

## Packages

| Package | Role |
|---------|------|
| `Pragmatic.Identity` | `SystemUser`, identity options, claim mapping — no ASP.NET Core dependency |
| `Pragmatic.Identity.AspNetCore` | `ClaimsPrincipal` → `ICurrentUser`, header-based development identity |
| `Pragmatic.Identity.Local` | Self-contained local identity: signup, login, password reset (`LocalIdentityPackage`) |
| `Pragmatic.Identity.Local.Jwt` | JWT issuing and bearer authentication — `UseJwtAuthentication()` |
| `Pragmatic.Identity.Oidc` | Bearer tokens from any external OpenID Connect provider — `UseOidcAuthentication()` |
| `Pragmatic.Identity.Keycloak` | Keycloak realm roles, plus an admin client for provisioning — `UseKeycloakAuthentication()` |
| `Pragmatic.Identity.Persistence` | EF Core stores for roles, groups and permissions, with temporal assignments |
| `Pragmatic.Identity.Auditing` | Failed logins and lockouts on the [Audit](../Pragmatic.Audit/README.md) trail, pseudonymised — `AddIdentitySecurityAuditing()` |

`ICurrentUser` itself lives in `Pragmatic.Abstractions`, so a library can consume it without any of
these.

## Installation

```bash
dotnet add package Pragmatic.Identity
dotnet add package Pragmatic.Identity.Local.Jwt   # optional: JWT authentication
```

## Quick Start

```csharp
// JWT from the Jwt section of the configuration: Key, Issuer, Audience
app.UseJwtAuthentication();

// consume the current user anywhere — no IHttpContextAccessor, no magic strings
public class Handler(ICurrentUser user) { /* user.Id, user.TenantId, user.Authorization.HasPermission(...) */ }
```

Full walkthrough: [Getting Started](docs/getting-started.md).

## What you get

- **`ICurrentUser`** — identity, authorization, authentication sub-objects; multi-value claims, tenant,
  impersonation; works in HTTP, jobs, and tests.
- **Local identity** — signup/login/password-reset actions (`Pragmatic.Identity.Local`).
- **JWT** — bearer authentication with configurable issuer/audience/expiry.
- **Users** — a plain entity marked `[PragmaticUser]`; the generator owns its identity members and EF
  configuration.
- **Persistence** — EF stores for roles, groups and permissions, with temporal assignments. There is no
  JIT provisioning: creating a local record on first external login is not implemented (see
  [Persistence](docs/persistence.md)).
- **Authorization integration** — feeds [Authorization](../Pragmatic.Authorization/README.md)'s permission resolution.

## Status

**Functional** within 1.0.0-alpha — `ICurrentUser`, local identity, JWT, and EF-backed persistence. See
the [roadmap](../docs/ROADMAP.md).

## Documentation

| Guide | What you'll learn |
|-------|-------------------|
| [Concepts](docs/concepts.md) | `ICurrentUser` model, claim mapping, accessors, the permission chain |
| [Getting Started](docs/getting-started.md) | Wire authentication, consume the current user |
| [Packages](docs/packages.md) | The core packages and how they layer |
| [JWT Authentication](docs/jwt-authentication.md) | Bearer tokens, issuer/audience/expiry, signing keys |
| [Dev Authentication](docs/dev-authentication.md) | Header-based auth for development and tests |
| [Persistence](docs/persistence.md) | EF stores, temporal roles, external identities, `[PragmaticUser]` |
| [Common Mistakes](docs/common-mistakes.md) | The most frequent identity pitfalls |
| [Troubleshooting](docs/troubleshooting.md) | Problem/solution guide |

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](../README.md) ecosystem — see [Licensing](../docs/LICENSING.md).
Pragmatic.Identity is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
