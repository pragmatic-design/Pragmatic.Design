---
title: "Development Authentication"
description: "Guide to using `HeaderUserMiddleware` and `NoOpAuthenticationHandler` for development and integration testing."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Identity/docs/dev-authentication.md
sidebar:
  order: 3
---
Guide to using `HeaderUserMiddleware` and `NoOpAuthenticationHandler` for development and integration testing.

## Overview

During development you rarely want to set up a full identity provider. Pragmatic.Identity provides two components that work together for header-based authentication:

1. **`HeaderUserMiddleware`** -- reads HTTP headers and creates a `ClaimsPrincipal`
2. **`NoOpAuthenticationHandler`** -- trusts the identity set by the middleware (or returns "no result" when absent)

This combination lets you test any user identity, role, and permission by simply setting HTTP headers.

## Setup

```csharp
using Pragmatic.Identity;
using Pragmatic.Composition.Hosting;

await PragmaticApp.RunAsync(args, app =>
{
    app.UseAuthentication<NoOpAuthenticationHandler>("PragmaticDefault");
});
```

The `HeaderUserMiddleware` must be added manually to the pipeline in your `IStartupStep.ConfigurePipeline`. It must run **before** `UseAuthentication()`. For example, in the Showcase app:

```csharp
public void ConfigurePipeline(IApplicationBuilder app)
{
    // Identity from headers (development/test); must run before UseAuthentication()
    app.UseMiddleware<Pragmatic.Identity.HeaderUserMiddleware>();

    app.UseAuthentication();
    app.UseAuthorization();
}
```

### Config-Driven (Recommended)

Use JWT in production, headers in development:

```csharp
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
```

When no `Jwt:Key` is present in configuration (typical for `Development` environment), the app falls back to header-based auth.

## HTTP Headers

### Supported Headers

| Header | Required | Description | Example |
|--------|:---:|-------------|---------|
| `X-User-Id` | Yes | User's unique identifier. If absent, request is anonymous. | `user-42` |
| `X-User-Name` | No | Display name. Defaults to `"Unknown"`. | `Jane Doe` |
| `X-User-Roles` | No | Comma-separated role names. | `admin,booking-manager` |
| `X-User-Permissions` | No | Comma-separated permission names. | `reservation.create,reservation.read` |
| `X-User-Tenant` | No | Tenant identifier for multi-tenancy. | `acme-corp` |
| `X-User-Groups` | No | Comma-separated group names. | `customer-care,vip-support` |

### How Headers Map to Claims

The middleware creates a `ClaimsIdentity` with authentication type `"HeaderAuth"` and maps headers to claims using `IdentityOptions`:

| Header | Claim Type | Configurable Via |
|--------|-----------|------------------|
| `X-User-Id` | `IdentityOptions.UserIdClaimType` (default: `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`) | Yes |
| `X-User-Name` | `IdentityOptions.DisplayNameClaimType` (default: `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name`) | Yes |
| `X-User-Roles` | `IdentityOptions.RoleClaimType` (default: `http://schemas.microsoft.com/ws/2008/06/identity/claims/role`) | Yes |
| `X-User-Permissions` | `IdentityOptions.PermissionClaimType` (default: `permission`) | Yes |
| `X-User-Tenant` | `IdentityOptions.TenantClaimType` (default: `tenant_id`) | Yes |
| `X-User-Groups` | `group` (hardcoded) | No |

Multi-valued headers (roles, permissions, groups) are split by comma. Each value becomes a separate claim.

### Claim Normalization

`ClaimsPrincipalUserAccessor` normalizes these claim types to short keys when building the `Claims` dictionary:

| Long Claim Type | Normalized Key |
|-----------------|---------------|
| `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier` | `sub` |
| `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name` | `name` |
| `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` | `role` |
| `permission` | `permission` |
| `tenant_id` | `tenant_id` |

This means `ICurrentUser.Claims["role"]` works regardless of whether the claim came from a JWT, OIDC provider, or header middleware.

## Request Examples

### Authenticated User with Roles

```http
GET /api/reservations HTTP/1.1
X-User-Id: user-42
X-User-Name: Jane Doe
X-User-Roles: booking-manager,catalog-viewer
X-User-Tenant: acme-corp
```

This creates an `ICurrentUser` with:
- `Id` = `"user-42"`
- `DisplayName` = `"Jane Doe"`
- `IsAuthenticated` = `true`
- `Kind` = `PrincipalKind.User`
- `TenantId` = `"acme-corp"`
- `Authorization.Roles` = `["booking-manager", "catalog-viewer"]`

### Authenticated User with Direct Permissions

```http
POST /api/reservations HTTP/1.1
X-User-Id: user-42
X-User-Name: Jane Doe
X-User-Permissions: reservation.create,reservation.read,guest.read
```

### Admin with Wildcard Permission

```http
GET /api/admin/users HTTP/1.1
X-User-Id: admin-1
X-User-Name: Admin
X-User-Roles: admin
X-User-Permissions: *
```

### Multi-Tenant with Groups

```http
GET /api/bookings HTTP/1.1
X-User-Id: user-100
X-User-Name: Bob Smith
X-User-Roles: receptionist
X-User-Tenant: hotel-alpha
X-User-Groups: front-desk,morning-shift
```

### Anonymous Request (No Authentication)

```http
GET /api/public/status HTTP/1.1
```

Without `X-User-Id`, the middleware does nothing. `ICurrentUser` resolves to the default (anonymous or whatever is registered).

## Testing Patterns

### Integration Tests with WebApplicationFactory

Use the `HeaderUserMiddleware` to authenticate test requests:

```csharp
public class ReservationTests(ShowcaseWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task CreateReservation_WithPermission_Succeeds()
    {
        var client = Factory.CreateClient();

        // Set identity headers
        client.DefaultRequestHeaders.Add("X-User-Id", "test-user");
        client.DefaultRequestHeaders.Add("X-User-Name", "Test User");
        client.DefaultRequestHeaders.Add("X-User-Roles", "booking-manager");
        client.DefaultRequestHeaders.Add("X-User-Permissions", "reservation.create");
        client.DefaultRequestHeaders.Add("X-User-Tenant", "test-tenant");

        var response = await client.PostAsJsonAsync("/api/reservations", new
        {
            GuestId = guestId,
            CheckIn = "2026-04-01",
            CheckOut = "2026-04-05"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

### Helper Extension for Test Projects

Create a helper to reduce boilerplate:

```csharp
public static class HttpClientTestExtensions
{
    public static HttpClient AsUser(
        this HttpClient client,
        string userId,
        string? name = null,
        string[]? roles = null,
        string[]? permissions = null,
        string? tenantId = null,
        string[]? groups = null)
    {
        client.DefaultRequestHeaders.Remove("X-User-Id");
        client.DefaultRequestHeaders.Remove("X-User-Name");
        client.DefaultRequestHeaders.Remove("X-User-Roles");
        client.DefaultRequestHeaders.Remove("X-User-Permissions");
        client.DefaultRequestHeaders.Remove("X-User-Tenant");
        client.DefaultRequestHeaders.Remove("X-User-Groups");

        client.DefaultRequestHeaders.Add("X-User-Id", userId);

        if (name is not null)
            client.DefaultRequestHeaders.Add("X-User-Name", name);

        if (roles is { Length: > 0 })
            client.DefaultRequestHeaders.Add("X-User-Roles", string.Join(",", roles));

        if (permissions is { Length: > 0 })
            client.DefaultRequestHeaders.Add("X-User-Permissions", string.Join(",", permissions));

        if (tenantId is not null)
            client.DefaultRequestHeaders.Add("X-User-Tenant", tenantId);

        if (groups is { Length: > 0 })
            client.DefaultRequestHeaders.Add("X-User-Groups", string.Join(",", groups));

        return client;
    }

    public static HttpClient AsAdmin(this HttpClient client)
        => client.AsUser("admin-1", "Admin", roles: ["admin"], permissions: ["*"]);

    public static HttpClient AsAnonymous(this HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("X-User-Id");
        client.DefaultRequestHeaders.Remove("X-User-Name");
        client.DefaultRequestHeaders.Remove("X-User-Roles");
        client.DefaultRequestHeaders.Remove("X-User-Permissions");
        client.DefaultRequestHeaders.Remove("X-User-Tenant");
        client.DefaultRequestHeaders.Remove("X-User-Groups");
        return client;
    }
}
```

Usage:

```csharp
var client = Factory.CreateClient()
    .AsUser("user-42", "Jane", roles: ["booking-manager"], tenantId: "acme");

var response = await client.GetAsync("/api/reservations");
```

### Testing Permission Denial

```csharp
[Fact]
public async Task DeleteReservation_WithoutPermission_Returns403()
{
    var client = Factory.CreateClient()
        .AsUser("user-42", permissions: ["reservation.read"]);  // No delete permission

    var response = await client.DeleteAsync($"/api/reservations/{id}");

    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
```

### Testing Anonymous Access

```csharp
[Fact]
public async Task GetReservation_AsAnonymous_Returns401()
{
    var client = Factory.CreateClient().AsAnonymous();

    var response = await client.GetAsync($"/api/reservations/{id}");

    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
}
```

### Testing with .http Files (REST Client)

Create `.http` files for manual testing:

```http
### Login as admin
GET {{baseUrl}}/api/reservations
X-User-Id: admin-1
X-User-Name: Admin
X-User-Roles: admin
X-User-Permissions: *

### Login as booking manager
GET {{baseUrl}}/api/reservations
X-User-Id: manager-1
X-User-Name: Manager
X-User-Roles: booking-manager
X-User-Tenant: hotel-alpha

### Login as read-only user
GET {{baseUrl}}/api/reservations
X-User-Id: reader-1
X-User-Name: Reader
X-User-Permissions: reservation.read,guest.read

### Anonymous request
GET {{baseUrl}}/api/public/health
```

## How It Works Internally

### Request Pipeline Flow

```
HTTP Request with X-User-Id header
    |
    v
HeaderUserMiddleware.InvokeAsync()
    |  Reads X-User-Id, X-User-Name, X-User-Roles, X-User-Permissions, X-User-Tenant, X-User-Groups
    |  Creates ClaimsIdentity with auth type "HeaderAuth"
    |  Sets HttpContext.User = new ClaimsPrincipal(identity)
    |
    v
UseAuthentication() middleware
    |
    v
NoOpAuthenticationHandler.HandleAuthenticateAsync()
    |  Checks if Context.User.Identity.IsAuthenticated
    |  If yes: returns AuthenticateResult.Success (trusts the identity)
    |  If no: returns AuthenticateResult.NoResult (unauthenticated)
    |
    v
UseAuthorization() middleware
    |  Evaluates policies (RequireAuthenticated, RequirePermission, etc.)
    |
    v
Endpoint handler
    |  ICurrentUser resolved by ClaimsPrincipalUserAccessor
    |  Reads claims from HttpContext.User
```

### Key Detail: Middleware Order

`HeaderUserMiddleware` must run **before** `UseAuthentication()`. You register it manually in your `IStartupStep.ConfigurePipeline`. Ensure correct order:

```csharp
app.UseMiddleware<HeaderUserMiddleware>();  // First: set identity from headers
app.UseAuthentication();                    // Second: authenticate
app.UseAuthorization();                     // Third: authorize
```

### Logging

`HeaderUserMiddleware` logs at `Debug` level when it resolves a user from headers:

```
dbug: Pragmatic.Identity.HeaderUserMiddleware[0]
      User 'user-42' resolved from headers for /api/reservations
```

## Security Warning

`HeaderUserMiddleware` and `NoOpAuthenticationHandler` are **development and testing only, enforced in
code, not by convention**. Both check `IHostEnvironment.IsDevelopment()` and **throw
`InvalidOperationException`** the moment they run outside the `Development` environment:

- `HeaderUserMiddleware` throws on the first request it handles in a non-Development environment.
- `NoOpAuthenticationHandler` throws from `HandleAuthenticateAsync` in a non-Development environment.

This is a fail-closed guard: a build that accidentally ships either component to Staging/Production
fails loudly on the first request rather than silently trusting spoofable `X-User-*` headers. In
production:

- Use a real authentication handler (JWT, OIDC, etc.)
- Never route `X-User-*` headers from external traffic to the app
- The config-driven pattern (JWT key present = JWT auth, absent = dev auth) is the recommended approach:
  it keeps the dev-only components out of the pipeline entirely when a signing key is configured
