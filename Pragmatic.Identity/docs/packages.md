# Package Architecture

The Pragmatic.Identity stack is split into five core packages, each with a clear responsibility and minimal dependencies, plus three integration packages (OIDC, Keycloak, security auditing; see [Integration Packages](#integration-packages)). This document describes each package in detail.

## Layer Diagram

```
Layer 0 (Abstractions)      Layer 1 (Core)           Layer 2 (ASP.NET)        Layer 3 (Features)
Pragmatic.Abstractions  -->  Pragmatic.Identity  -->  Identity.AspNetCore  -->  Identity.Local.Jwt
  ICurrentUser                SystemUser               ClaimsPrincipalUser      JwtTokenGenerator
  IUserAuthorization          IdentityOptions          HeaderUserMiddleware     UseJwtAuthentication
  IAuthenticationContext      ClaimsPermissionChecker   NoOpAuthHandler
  PrincipalKind               IdentityRecord           PragmaticPermHandler
  AnonymousUser

                                                                                Identity.Local
                                                                                  RegisterUser
                                                                                  LoginUser
                                                                                  SignInUser
                                                                                  BcryptHasher

Pragmatic.Authorization  ------------------------------------------>  Identity.Persistence
  CachedPermissionResolver                                              EfRolePermissionStore
  IPermissionProvider                                                   EfGroupRoleStore
  IRolePermissionStore
```

## Package Details

### 1. Pragmatic.Identity

**NuGet**: `Pragmatic.Identity`
**Dependencies**: `Pragmatic.Abstractions`
**ASP.NET Core**: No
**EF Core**: No

The core package that provides identity primitives without any web framework dependency. Use this package when you need identity concepts in a library or non-ASP.NET project.

#### Key Types

| Type | Description |
|------|-------------|
| `SystemUser` | Singleton `ICurrentUser` for background jobs and system operations. Always authenticated with `FullAccessUserAuthorization`. |
| `IdentityOptions` | Configures which claim types map to user ID, display name, role, permission, and tenant. |
| `IdentityRecord` | Abstract base class for identity provider records (local, Keycloak, etc.). Contains `ExternalIdentityKey`, `ProvisionSource`, `IsActive`, `LastLoginAt`, plus `IAuditable` fields. |
| `ClaimsPermissionChecker` | Wraps `ICurrentUser.Authorization` as async `IPermissionChecker` for consumers that require the async interface. |
| `ProvisionSource` | Enum: `Manual`, `Jit`, `Scim`. |

#### When to Reference

- You are building a library that depends on `ICurrentUser` but not ASP.NET Core.
- You need `SystemUser` for background job contexts.
- You are implementing a custom identity provider that extends `IdentityRecord`.

---

### 2. Pragmatic.Identity.AspNetCore

**NuGet**: `Pragmatic.Identity.AspNetCore`
**Dependencies**: `Pragmatic.Identity`, ASP.NET Core framework reference
**ASP.NET Core**: Yes
**EF Core**: No

The ASP.NET Core bridge. Maps `HttpContext.User` (a `ClaimsPrincipal`) to the Pragmatic `ICurrentUser` interface. Also provides development-time authentication tools.

#### Key Types

| Type | Description |
|------|-------------|
| `ClaimsPrincipalUserAccessor` | Scoped `ICurrentUser` that reads from `HttpContext.User`. Claim types are configurable via `IdentityOptions`. Normalizes long-form claim URIs to short names (`role`, `permission`, `tenant_id`, `sub`, `name`). |
| `ClaimsAuthenticationContext` | Reads `IAuthenticationContext` from claims (`iss`, `sub`, `acr`, `amr`, `auth_time`, `exp`). |
| `HeaderUserMiddleware` | Development middleware that creates a `ClaimsPrincipal` from HTTP headers (`X-User-Id`, `X-User-Name`, `X-User-Roles`, `X-User-Permissions`, `X-User-Tenant`, `X-User-Groups`). **Throws `InvalidOperationException` outside the Development environment.** |
| `NoOpAuthenticationHandler` | Development auth handler that trusts identities set by earlier middleware. Returns `NoResult` when no identity is present. **Throws `InvalidOperationException` outside the Development environment.** |
| `PragmaticPermissionHandler` | `AuthorizationHandler` that delegates permission checks to `IPermissionChecker`. |

⚠️ The requirement it handles, `PragmaticPermissionRequirement`, and its `PermissionMode` live in
**`Pragmatic.Endpoints.AspNetCore`**. What an endpoint requires is declared where the endpoint is
(every boundary with an `[Endpoint]` already has that package), and only the evaluation, which needs
`IPermissionChecker` resolved from the container, is host-side and lives here.

#### DI Registration

```csharp
// Registers ICurrentUser, IPermissionChecker, IAuthorizationHandler
services.AddPragmaticIdentity();

// With custom claim mapping
services.AddPragmaticIdentity(opts =>
{
    opts.UserIdClaimType = "sub";
    opts.PermissionClaimType = "permissions";
});
```

#### IPragmaticBuilder Extensions

```csharp
// Custom auth handler
app.UseAuthentication<NoOpAuthenticationHandler>("PragmaticDefault");

// Full control via AuthenticationBuilder
app.UseAuthentication(auth => auth.AddJwtBearer(...));
```

Both extensions call `AddAuthorization()` and `AddPragmaticAuthorization()` automatically.

#### When to Reference

- Any Pragmatic.Design web application (this is the standard web integration).
- You want development-time header-based authentication.
- You need the permission authorization handler bridge.

---

### 3. Pragmatic.Identity.Local

**NuGet**: `Pragmatic.Identity.Local`
**Dependencies**: `Pragmatic.Identity`, `Pragmatic.Actions`, `Pragmatic.SourceGenerator` (analyzer)
**ASP.NET Core**: No
**EF Core**: No

Self-hosted identity provider. Manages local credentials (email + password) with domain actions for registration, login, and password management. The source generator exposes these actions as HTTP endpoints when the host references this package.

#### Domain Actions

| Action | Type | Input | Output | Errors |
|--------|------|-------|--------|--------|
| `RegisterUser` | `DomainAction<string>` | `Email`, `Password` | `ExternalIdentityKey` | `EmailAlreadyExistsError`, `PasswordPolicyError` |
| `LoginUser` | `DomainAction<LoginResult>` | `Email`, `Password` | `LoginResult` (key + timestamp) | `InvalidCredentialsError`, `AccountLockedError`, `IdentityNotActiveError`, `EmailNotVerifiedError` |
| `SignInUser` | `DomainAction<AccessToken>` | `Email`, `Password` | `AccessToken` (`Token` + `ExpiresAt`), signed by `IAccessTokenIssuer` with what each `IUserClaimsContributor` adds | same as `LoginUser` |
| `ChangePassword` | `VoidDomainAction` | `CurrentPassword`, `NewPassword` | void | `InvalidCredentialsError`, `IdentityNotActiveError`, `PasswordPolicyError` |
| `RequestPasswordReset` | `VoidDomainAction` | `Email` | void | (none -- never reveals email existence) |
| `ConfirmPasswordReset` | `VoidDomainAction` | `Email`, `Token`, `NewPassword` | void | `InvalidResetTokenError`, `PasswordPolicyError` |
| `RequestEmailVerification` | `VoidDomainAction` | `Email` | void | (none -- never reveals email existence/state) |
| `ConfirmEmail` | `VoidDomainAction` | `Email`, `Token` | void | `InvalidEmailVerificationTokenError` |

> `ChangePassword` is gated by `[RequirePermission(LocalIdentityPermissions.ChangePassword)]`: it acts
> on the currently authenticated identity (resolved from `ICurrentUser.Authentication.ExternalIdentityKey`).
>
> `RequestPasswordReset` and `RequestEmailVerification` **do not return the token**. They generate a
> token, store only its hash, and hand the plaintext to a notifier for out-of-band delivery (email/SMS).
> `LoginUser` returns `EmailNotVerifiedError` only when `LocalIdentityOptions.RequireEmailVerification`
> is enabled and the account's email is unverified. Successful `ChangePassword`/`ConfirmPasswordReset`
> rotate the identity's `SecurityStamp`, invalidating tokens issued before the change.

#### Service Interfaces

| Interface | Default Implementation | Description |
|-----------|----------------------|-------------|
| `ILocalIdentityStore` | Generated | Loads and saves the `LocalIdentity` owned by the `[PragmaticUser]` entity; the generator writes `{User}.LocalIdentityStore` when the project references `Pragmatic.Persistence.EFCore`. A class of your own implementing the interface replaces it. |
| `IPasswordHasher` | `BcryptPasswordHasher` | BCrypt with configurable work factor (default: 12). |
| `ISecurityTokenService` | `HmacSecurityTokenService` | SHA-256 based token generation and verification. |
| `IPasswordPolicy` | `DefaultPasswordPolicy` | Minimum length validation (default: 8 chars). Replace for complexity/history/breach checks. |
| `IPasswordResetNotifier` | `LogOnlyPasswordResetNotifier` | Delivers the reset token out-of-band. **The default does NOT deliver**: it logs a warning so a misconfigured app fails loud. Register a real email/SMS notifier in production. |
| `IEmailVerificationNotifier` | `LogOnlyEmailVerificationNotifier` | Delivers the email-verification token out-of-band. Same **log-only, non-delivering** default; replace in production. |

#### LocalIdentity Entity

Extends `IdentityRecord` with password-specific fields:

| Property | Type | Description |
|----------|------|-------------|
| `PasswordHash` | `string` | BCrypt hash of the password |
| `PasswordSalt` | `string?` | External salt (if algorithm requires it) |
| `Email` | `string?` | Email address for login and reset |
| `EmailVerified` | `bool` | Whether email has been verified |
| `ResetToken` | `string?` | Hashed reset token |
| `ResetTokenExpiresAt` | `DateTimeOffset?` | Token expiry time |
| `FailedLoginAttempts` | `int` | Consecutive failed attempts |
| `LockoutEnd` | `DateTimeOffset?` | Account lockout expiry |

#### Configuration

```csharp
services.Configure<LocalIdentityOptions>(opts =>
{
    opts.PasswordWorkFactor = 12;          // BCrypt rounds
    opts.MaxFailedLoginAttempts = 5;       // Before lockout
    opts.LockoutDuration = TimeSpan.FromMinutes(15);
    opts.ResetTokenExpiry = TimeSpan.FromHours(1);
    opts.MinPasswordLength = 8;
    opts.RequireEmailVerification = false;
});
```

#### Domain Events

All actions raise domain events via `IDomainEventDispatcher`:

| Event | Raised When |
|-------|-------------|
| `UserRegistered` | New user created |
| `UserLoggedIn` | Successful authentication |
| `LoginFailed` | Failed attempt (wrong password, inactive, unknown email) |
| `AccountLocked` | Failed attempts reached threshold |
| `AccountUnlocked` | A password reset cleared an active lockout / failed-attempt counter |
| `PasswordChanged` | Password updated |
| `PasswordResetCompleted` | Reset token consumed and password changed |

#### Security Features

- **BCrypt hashing**: Adaptive work factor, resistant to GPU attacks
- **Account lockout**: Configurable threshold and duration
- **Reset token hashing**: Tokens stored as SHA-256 hashes, not plaintext; the plaintext is delivered only via the notifier, never in the API response
- **Timing-safe comparison**: `CryptographicOperations.FixedTimeEquals` for token verification, with equal-cost paths on the not-found branch so response timing does not leak email existence
- **Email existence hiding**: `RequestPasswordReset` / `RequestEmailVerification` always return success
- **Security stamp rotation**: `ChangePassword` / `ConfirmPasswordReset` reset the `SecurityStamp`, invalidating sessions/tokens minted before the change
- **Optional email verification**: `LocalIdentityOptions.RequireEmailVerification` blocks login until the address is confirmed

#### Package Definition

`LocalIdentityPackage` implements `IPackageDefinition`:
- **Route prefix**: `identity/local`
- **Required types**: `DomainActionAttribute`, `IDomainEventDispatcher`

#### When to Reference

- You want self-hosted credential management (no external IdP dependency).
- You need registration/login as domain actions in your Pragmatic pipeline.
- You are building a standalone app or microservice with local accounts.

---

### 4. Pragmatic.Identity.Local.Jwt

**NuGet**: `Pragmatic.Identity.Local.Jwt`
**Dependencies**: `Pragmatic.Identity.Local`, `Pragmatic.Identity.AspNetCore`, `Microsoft.AspNetCore.Authentication.JwtBearer`
**ASP.NET Core**: Yes
**EF Core**: No

JWT token generation and validation for local identity. This is the "top" package -- referencing it brings in the entire Identity.Local stack with JWT support.

#### Key Types

| Type | Description |
|------|-------------|
| `JwtTokenGenerator` | Creates JWT tokens with configurable claims. Accepts subject, display name, tenant, roles, and permissions. |
| `JwtOptions` | Configuration: `SigningKey` (required, 256+ bits), `Issuer`, `Audience`, `TokenExpiration` (default 1h), `ClockSkew` (default 1min). |
| `IAccessTokenIssuer` (from `Identity.Local`) | Implemented by `JwtTokenGenerator`, registered by `UseJwtAuthentication()`: it signs `SignInUser`'s `AccessToken`, the account's key and security stamp always included. |

#### Token Claims

The `JwtTokenGenerator.Generate()` method embeds these claims in the JWT:

| Claim | Source | Always Present |
|-------|--------|:-:|
| `sub` | `ExternalIdentityKey` | Yes |
| `jti` | Random GUID | Yes |
| `iat` | Current timestamp (epoch) | Yes |
| `name` | `displayName` parameter | If provided |
| `tenant_id` | `tenantId` parameter | If provided |
| `role` | `roles` parameter (multi-valued) | If provided |
| `permission` | `permissions` parameter (multi-valued) | If provided |

#### IPragmaticBuilder Extension

```csharp
app.UseJwtAuthentication(jwt =>
{
    jwt.SigningKey = "your-256-bit-secret-key";  // Required, min 32 chars
    jwt.Issuer = "https://myapp.example.com";     // Optional, enables issuer validation
    jwt.Audience = "https://myapp.example.com";   // Optional, enables audience validation
    jwt.TokenExpiration = TimeSpan.FromHours(1);  // Default: 1 hour
    jwt.ClockSkew = TimeSpan.FromMinutes(1);      // Default: 1 minute
});
```

This single call:
1. Configures `JwtOptions` and registers `JwtTokenGenerator` as singleton
2. Sets up `JwtBearerDefaults.AuthenticationScheme` as default scheme
3. Configures token validation parameters (signing key, issuer, audience, lifetime)
4. Calls `AddAuthorization()` and `AddPragmaticAuthorization()`

#### When to Reference

- You want a complete local authentication stack with JWT tokens.
- You need both token generation (login) and token validation (API requests).
- You are building an API that issues its own tokens.

---

### 5. Pragmatic.Identity.Persistence

**NuGet**: `Pragmatic.Identity.Persistence`
**Dependencies**: `Pragmatic.Abstractions`, `Pragmatic.Authorization`, `Pragmatic.Temporal`, `Microsoft.EntityFrameworkCore`
**ASP.NET Core**: No
**EF Core**: Yes

Database-backed authorization stores with temporal validity. See [Persistence](persistence.md) for full details.

> **There is no user base class or identity `DbContext` to inherit.** Use
> `[UsePackage<LocalIdentityPackage>]` on the host module with a `[PragmaticUser]` entity. The EF
> stores and the temporal entities work alongside it. There is no Just-In-Time user provisioning.
> See [Persistence](persistence.md).

#### Key Types

| Type | Description |
|------|-------------|
| `EfRolePermissionStore` | `ITemporalRolePermissionStore` backed by `RolePermission` table. Filters by `ValidFrom`/`ValidTo` using `IClock`. |
| `EfGroupRoleStore` | `ITemporalGroupRoleStore` backed by `GroupRole` table. Same temporal filtering. |


| `IdentityPersistenceBuilder` | Fluent builder: `SkipRolePermissionStore()`, `SkipGroupRoleStore()`. |

#### Entities (all temporal with `ValidFrom`/`ValidTo`)

| Entity | Description |
|--------|-------------|

| `ExternalIdentityRecord<TKey>` | Links a user to an external IdP (issuer + subject) |
| `UserRole<TKey>` | User-to-role assignment with temporal validity |
| `UserGroup<TKey>` | User-to-group assignment with temporal validity |
| `RolePermission` | Role-to-permission mapping with temporal validity |
| `GroupRole` | Group-to-role mapping with temporal validity |

#### When to Reference

- You need runtime-manageable role/permission assignments (not just compile-time definitions).
- You want temporal authorization (permissions that expire or activate at specific times).
- You are building an admin panel for role/permission management.

JIT provisioning (creating a local record on first external login) is **not** in this package, or in
any other (see [Persistence](persistence.md)).

## Integration Packages

Three more packages sit on top of the five above:

| Package | Entry point | What it adds |
|---------|-------------|--------------|
| `Pragmatic.Identity.Oidc` | `UseOidcAuthentication()` | Bearer tokens from any OpenID Connect provider, validated through its discovery document; its role claims feed the authorization pipeline |
| `Pragmatic.Identity.Keycloak` | `UseKeycloakAuthentication()` | Keycloak on top of Oidc: `realm_access` roles, and an admin client for user provisioning and role synchronisation |
| `Pragmatic.Identity.Auditing` | `AddIdentitySecurityAuditing()` | Failed logins and lockouts recorded on the `Pragmatic.Audit` trail, pseudonymised: a failure against an unknown identity carries no subject reference |

## Package Selection Guide

| Scenario | Packages Needed |
|----------|----------------|
| Library that reads `ICurrentUser` | `Pragmatic.Abstractions` only |
| ASP.NET app with external IdP | `Identity.Oidc` (or `Identity.Keycloak`) |
| ASP.NET app with dev headers | `Identity.AspNetCore` |
| Self-hosted auth with JWT | `Identity.Local.Jwt` (pulls in everything) |
| Database-backed role management | `Identity.Persistence` + one of the above |
| Background job context | `Identity` (for `SystemUser`) |
| Integration tests with fake users | `Identity.AspNetCore` (for `HeaderUserMiddleware`) |
