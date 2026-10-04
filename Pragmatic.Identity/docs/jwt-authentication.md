# JWT Authentication

Complete guide to setting up JWT-based authentication with `Pragmatic.Identity.Local.Jwt`.

## Overview

The JWT package provides:
1. **Token generation** -- `JwtTokenGenerator` creates signed JWT tokens after successful login
2. **Token validation** -- ASP.NET Core JwtBearer handler validates tokens on incoming requests
3. **Claim mapping** -- `ClaimsPrincipalUserAccessor` maps JWT claims to `ICurrentUser`

All three are configured by a single call to `UseJwtAuthentication()`.

## Setup

### 1. Add Package Reference

```xml
<ItemGroup>
  <ProjectReference Include="path/to/Pragmatic.Identity.Local.Jwt.csproj" />
</ItemGroup>
```

This transitively includes:
- `Pragmatic.Identity.Local` (registration, login, password management)
- `Pragmatic.Identity.AspNetCore` (ClaimsPrincipal to ICurrentUser bridge)
- `Pragmatic.Identity` (core types)
- `Microsoft.AspNetCore.Authentication.JwtBearer`

### 2. Configure in Program.cs

```csharp
using Pragmatic.Identity.Local.Jwt;

await PragmaticApp.RunAsync(args, app =>
{
    app.UseJwtAuthentication();   // reads the Jwt section below
});
```

Without arguments it reads the `Jwt` section: `Key` (required: the host does not start without it,
and the message names the key), `Issuer`, `Audience`, and optionally `TokenExpiration` and `ClockSkew`
(TimeSpan, `00:20:00`) and `RequireSecurityStamp`. `UseJwtAuthentication("Auth:Tokens")` reads another
section. When the values are not in configuration, set them in code:

```csharp
app.UseJwtAuthentication(jwt =>
{
    jwt.SigningKey = secrets.JwtKey;
    jwt.Issuer = "https://myapp.example.com";
    jwt.Audience = "https://myapp.example.com";
    jwt.TokenExpiration = TimeSpan.FromHours(1);
});
```

### 3. Add Configuration

```json
// appsettings.json
{
  "Jwt": {
    "Key": "your-super-secret-key-that-is-at-least-32-characters-long!"
  }
}
```

For production, use a secrets manager:

```json
// appsettings.Production.json (or Azure Key Vault, AWS Secrets Manager, etc.)
{
  "Jwt": {
    "Key": "production-secret-from-vault",
    "Issuer": "https://api.myapp.com",
    "Audience": "https://api.myapp.com"
  }
}
```

### Sign-in rate limit

`UseJwtAuthentication` also caps sign-in attempts per client address: 10 a minute by default, on the
exposed `LoginUser` and `SignInUser` endpoints, recognised by the operation they run, whatever route the
application gave them. The per-account lockout stops guessing one account's password; this stops trying a
password against many accounts from one address. Refused attempts answer 429.

```json
"Identity": { "Local": { "RateLimit": { "PermitLimit": 10, "Window": "00:01:00", "Enabled": true } } }
```

The address is the connection's: behind a load balancer configure `UseForwardedHeaders` with the known
proxies, or every client shares one bucket. `X-Forwarded-For` is never read directly: a client could
rotate it to get a fresh bucket. Under `TestServer` there is no address at all, so a test host shares one
bucket across all its requests: raise `PermitLimit` for a suite that signs in often.

## JwtOptions Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SigningKey` | `string` (required) | -- | Symmetric signing key. Must be at least 32 characters (256 bits). Used with HMAC-SHA256. |
| `Issuer` | `string?` | `null` | Token issuer (JWT `iss` claim). When set, incoming tokens are validated against this value. |
| `Audience` | `string?` | `null` | Token audience (JWT `aud` claim). When set, incoming tokens are validated against this value. |
| `TokenExpiration` | `TimeSpan` | 1 hour | How long generated tokens are valid. |
| `ClockSkew` | `TimeSpan` | 1 minute | Tolerance for clock differences between token issuer and validator. |

## Token Generation

### The sign-in issues it

A local sign-in does not generate tokens by hand: the package's `SignInUser` checks the credentials and
signs the token through `IAccessTokenIssuer`, which `UseJwtAuthentication` registers as this package's
`JwtTokenGenerator`. See [Integration with Login Flow](#integration-with-login-flow).

A host that imports the package and calls no `UseJwtAuthentication` still starts: the package registers
`UnconfiguredAccessTokenIssuer`, which the host's issuer replaces. It signs nothing: a sign-in there
checks the credentials and then fails with an `InvalidOperationException` that names the call to make.

### Generate() Overloads

`JwtTokenGenerator` stays available for a token that is not a sign-in's: a service account, a test.
Depend on `IAccessTokenIssuer` where you can: it is the contract a module can see.

```csharp
// Full signature with explicit parameters
AccessToken Generate(
    string subject,
    string? displayName = null,
    string? tenantId = null,
    IEnumerable<string>? roles = null,
    IEnumerable<string>? permissions = null,
    string? securityStamp = null,
    string? externalIdentityKey = null);

// Convenience overload from LoginResult
AccessToken Generate(
    LoginResult loginResult,
    string? displayName = null,
    string? tenantId = null,
    IEnumerable<string>? roles = null,
    IEnumerable<string>? permissions = null,
    string? securityStamp = null);

// IAccessTokenIssuer: what a sign-in established, the stamp included
AccessToken Issue(SignInClaims claims);
```

### AccessToken

```csharp
public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);
```

- `Token` -- the complete JWT string, ready to send to the client
- `ExpiresAt` -- when the token expires (UTC)

## Token Structure

A generated JWT contains these claims:

```json
{
  "sub": "local|user@example.com",
  "jti": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "iat": 1711043200,
  "name": "Jane Doe",
  "tenant_id": "acme-corp",
  "role": ["booking-manager", "catalog-viewer"],
  "permission": ["reservation.create", "reservation.read"],
  "exp": 1711046800,
  "iss": "https://myapp.example.com",
  "aud": "https://myapp.example.com"
}
```

### Claim-to-ICurrentUser Mapping

When a request arrives with a valid JWT, the claims are mapped as follows:

| JWT Claim | ICurrentUser Property | Notes |
|-----------|----------------------|-------|
| `sub` | `Id` | Via `IdentityOptions.UserIdClaimType`; JwtBearer renames `sub` to the default URI on the way in |
| `name` | `DisplayName` | Via `IdentityOptions.DisplayNameClaimType`, which `UseJwtAuthentication()` sets to `name` |
| `role` | `Authorization.Roles` | Multi-valued; via `IdentityOptions.RoleClaimType`; JwtBearer renames `role` to the default URI |
| `permission` | Direct in `Claims["permission"]` | Also fed to `CachedPermissionResolver` |
| `tenant_id` | `TenantId` | Via `IdentityOptions.TenantClaimType` |
| `iss` | `Authentication.Issuer` | |
| `sub` | `Authentication.Subject` | |
| `exp` | `Authentication.ExpiresAt` | Parsed from Unix epoch |
| `auth_time` | `Authentication.AuthenticatedAt` | If present |
| `amr` | `Authentication.IsMfaAuthenticated` | Checks for "mfa" in `amr` claim |

### Claim Type Normalization

`ClaimsPrincipalUserAccessor` normalizes the claim types `IdentityOptions` names to short keys:

| Claim type (`IdentityOptions` default) | Normalized To |
|---|---|
| `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier` | `sub` |
| `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name` | `name` |
| `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` | `role` |
| Custom `permission` claim type | `permission` |
| Custom `tenant_id` claim type | `tenant_id` |

This normalization ensures that `ICurrentUser.Claims` uses consistent short keys regardless of whether the claim came from a JWT, OIDC provider, or header middleware.

> ⚠️ **The name is the one claim JwtBearer does not rename.** It turns `sub` and `role` into the two
> URIs above, and leaves `name` as `name`. So `UseJwtAuthentication()` sets
> `IdentityOptions.DisplayNameClaimType` to `name`, and `UseKeycloakAuthentication()` and
> `UseOidcAuthentication()` set it to the claim their validator names the user by (`preferred_username`,
> `OidcOptions.NameClaim`). `User.Identity.Name` and `ICurrentUser.DisplayName` therefore read the same
> claim. An application that wants another claim registers its own `Configure<IdentityOptions>` after
> the entry point, and it wins.

## Token Validation

`UseJwtAuthentication()` configures the following validation parameters:

| Parameter | Behavior |
|-----------|----------|
| `ValidateIssuerSigningKey` | Always `true` -- the signing key is always validated |
| `IssuerSigningKey` | `SymmetricSecurityKey` from `JwtOptions.SigningKey` |
| `ValidateIssuer` | `true` if `JwtOptions.Issuer` is set |
| `ValidIssuer` | `JwtOptions.Issuer` |
| `ValidateAudience` | `true` if `JwtOptions.Audience` is set |
| `ValidAudience` | `JwtOptions.Audience` |
| `ValidateLifetime` | Always `true` -- expired tokens are rejected |
| `ClockSkew` | `JwtOptions.ClockSkew` (default 1 minute) |
| `NameClaimType` | `"name"`, the claim `JwtTokenGenerator` writes the display name to |
| `RoleClaimType` | `ClaimTypes.Role`, the name JwtBearer gives `role` on the way in, so `IsInRole` and `[Authorize(Roles = …)]` see the roles |

## Custom Claim Providers

### Adding Custom Claims to Tokens

What the token says about the user beyond the account is contributed by the application at sign-in, with
an `IUserClaimsContributor`: a display name, a tenant, roles, direct permissions, or a subject other than
the account's key (a pseudonym, so that nothing recording who acted records an email):

```csharp
public sealed class UserProfileClaims(IReadRepository<UserProfile> profiles) : IUserClaimsContributor
{
    public async ValueTask ContributeAsync(LocalIdentity identity, SignInClaims claims, CancellationToken ct = default)
    {
        var key = identity.ExternalIdentityKey;
        var profile = await profiles.FirstOrDefaultAsync(Spec<UserProfile>.Where(p => p.IdentityKey == key), ct);
        if (profile is null)
            return;

        claims.DisplayName = profile.DisplayName;
        foreach (var role in profile.Roles)
            claims.Roles.Add(role);
    }
}
```

The account's key and the security stamp come from the identity and cannot be changed by a contributor.

### Reading Custom Claims from ICurrentUser

All JWT claims are available via `ICurrentUser.Claims`:

```csharp
public class SomeService(ICurrentUser currentUser)
{
    public string? GetDepartment()
    {
        return currentUser.Claims.TryGetValue("department", out var values)
            ? values.FirstOrDefault()
            : null;
    }
}
```

## Integration with Login Flow

The package signs a user in: `SignInUser` checks the credentials exactly as `LoginUser` does (lockout,
timing equalisation, the password rehash, the events) and answers with the `AccessToken`. The token
carries the account's key and its security stamp always, and what the application's
`IUserClaimsContributor`s add. Expose it from the module that imports the package:

```csharp
[UsePackage<LocalIdentityPackage, AppBoundary>]
[ExposeEndpoint<SignInUser>(HttpVerb.Post, "sign-in", AllowAnonymous = true)]   // POST identity/local/sign-in
public sealed class AppModule;
```

```csharp
// Host
app.UseJwtAuthentication();
app.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IUserClaimsContributor, UserProfileClaims>());
```

⚠️ A hand-written sign-in that calls `LoginUser` and then `JwtTokenGenerator.Generate` has to pass the
identity's `SecurityStamp` itself: the token it issues otherwise is refused on first use
(`RequireSecurityStamp`, the default). That is the step `SignInUser` does not leave to anyone.

## Security Considerations

### Signing Key

- Use at least 256 bits (32 characters) for HMAC-SHA256
- Store the key in a secrets manager, not in source code or `appsettings.json`
- Rotate keys periodically; consider a key rotation strategy for production

### Token Lifetime

- Default is 1 hour, which is reasonable for API tokens
- Shorter lifetimes (15-30 minutes) are more secure but require refresh token logic
- The `ClockSkew` setting (default 1 minute) prevents rejecting tokens from servers with minor clock differences

### Permission Embedding

There are two strategies for permissions in JWT tokens:

1. **Embed permissions in token** -- fast (no DB lookup per request), but token grows with permission count, and changes require re-login
2. **Embed only roles in token** -- smaller token, but requires `RoleExpansionProvider` + `IRolePermissionStore` to resolve permissions per request

For most applications, embedding roles and using the expansion provider chain is the recommended approach. Use `Identity.Persistence` with `EfRolePermissionStore` for runtime-manageable mappings.

### HTTPS

Always use HTTPS in production. JWT tokens sent over HTTP can be intercepted and replayed.
