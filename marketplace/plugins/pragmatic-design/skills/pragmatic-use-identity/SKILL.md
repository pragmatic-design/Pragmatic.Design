---
name: pragmatic-use-identity
description: Use when choosing authentication, adding sign-in, issuing or validating JWTs, connecting OIDC or Keycloak, or when every request is 401 (Pragmatic.Identity, local accounts, [PragmaticUser]). Permissions are pragmatic-use-authorization.
---

# Pragmatic Use Identity

**Covers:** Authenticate callers with Pragmatic.Identity: development headers, local accounts (sign-in, reset, lockout) with self-issued JWTs, or OIDC/Keycloak; the [PragmaticUser] entity, IUserClaimsContributor, SystemUser.

Identity answers **who is calling**; authorization (`pragmatic-use-authorization`) answers what they
may do. Code reads the caller through `ICurrentUser` (in `Pragmatic.Abstractions`) and never through
`HttpContext.User`, so a job, a message handler and a request look the same.

## Choose one: the build asks

Every endpoint requires an authenticated caller by default, and a host with no choice stops on
**PRAG1695**. The choices:

| The app… | Host | Packages |
|---|---|---|
| is being built, no real users yet | `if (app.Environment.IsDevelopment()) app.UseDevelopmentIdentity();` | `Pragmatic.Identity.AspNetCore` |
| keeps its own accounts (email + password) and signs its own tokens | `app.UseJwtAuthentication();` + the local package on a module | `Pragmatic.Identity.Local.Jwt` (brings Local and AspNetCore) |
| delegates sign-in to an OpenID Connect provider (Entra ID, Auth0, Okta…) | `app.UseOidcAuthentication(o => …)` | `Pragmatic.Identity.Oidc` |
| delegates to Keycloak, and may provision users there | `app.UseKeycloakAuthentication(k => …)` | `Pragmatic.Identity.Keycloak` |
| deliberately has no users (a LAN tool, behind an authenticating proxy) | `[AnonymousHost]` on the host `[Module]` | n/a |

⚠️ **A scheme configured for one environment only leaves the others without one**: the host refuses to
start there, naming the protected endpoints and the calls that would fix it. `dotnet run` without a
launch profile runs in **Production**, where `UseDevelopmentIdentity()` is off; keep
`Properties/launchSettings.json` with `ASPNETCORE_ENVIRONMENT=Development`.

⚠️ `Audience` is **mandatory outside Development** for JWT, OIDC and Keycloak: without it any token the
issuer minted for any other client would be accepted, so startup throws instead.

## Development identity

`UseDevelopmentIdentity()` trusts these headers, only in Development (outside it the middleware throws):

`X-User-Id`, `X-User-Name`, `X-User-Roles`, `X-User-Permissions`, `X-User-Tenant`, `X-User-Groups`.

The generated OpenAPI document and Scalar know the header, so the reference UI can call protected
routes. Tests use the same headers; see `pragmatic-use-testing` (`request.AsUser(...)`).

## Local accounts

The account lives **in your user entity**, as an owned `LocalIdentity` (its columns are in the user's
row), and the package's operations run inside your boundary:

```csharp
[Entity]
[PragmaticUser(MatchClaim = "sub")]
public partial class AppUser : IEntity
{
    public string DisplayName { get; private set; } = "";
    public LocalIdentity? Identity { get; set; }          // null until the account is opened
}

[Module(Name = "App.Accounts", Version = "1.0.0")]
[UsePackage<LocalIdentityPackage, AccountsBoundary>]
// The package proposes operations; the module decides which become routes (under identity/local/).
[ExposeEndpoint<SignInUser>(HttpVerb.Post, "sign-in", AllowAnonymous = true)]
[ExposeEndpoint<ChangePassword>(HttpVerb.Post, "change-password")]
[ExposeEndpoint<RequestPasswordReset>(HttpVerb.Post, "reset-password/request", AllowAnonymous = true)]
[ExposeEndpoint<ConfirmPasswordReset>(HttpVerb.Post, "reset-password/confirm", AllowAnonymous = true)]
public sealed class AccountsModule;
```

- **The store is generated.** A `[PragmaticUser]` entity that owns a `LocalIdentity`, in a project with
  `Pragmatic.Persistence.EFCore`, gets `{User}.LocalIdentityStore` registered as `ILocalIdentityStore`,
  saving through the boundary's unit of work. Write your own class implementing the interface only when
  the credentials live elsewhere; then nothing is generated.
- **Self-registration** (`RegisterUser`) needs a user to create: the entity implements
  `ISelfRegisteringUser<TUser>` with `static TUser Register(LocalIdentity identity)`. Without it the
  application provisions accounts itself (an administrator, an import), as Time off does.
- **Opening an account in code**: normalize the email with `LocalIdentity.NormalizeEmail`, compose the key
  with `ExternalIdentityKey.Compose(LocalIdentity.Provider, email)`, hash with `IPasswordHasher`, and give
  it a fresh `SecurityStamp`: the same shape `RegisterUser` produces, so the actions cannot tell them
  apart.

Operations: `SignInUser` (credentials → `AccessToken { Token, ExpiresAt }`), `LoginUser`
(credentials → `LoginResult`, no token), `RegisterUser`, `ChangePassword` (requires its permission, acts
on the current identity), `RequestPasswordReset` / `ConfirmPasswordReset`,
`RequestEmailVerification` / `ConfirmEmail`. Errors are typed: `InvalidCredentialsError`,
`AccountLockedError`, `IdentityNotActiveError`, `EmailNotVerifiedError`, `PasswordPolicyError`, …

```csharp
builder.Services.Configure<LocalIdentityOptions>(o =>
{
    o.MaxFailedLoginAttempts = 5;
    o.LockoutDuration = TimeSpan.FromMinutes(15);
    o.MinPasswordLength = 12;
    o.RequireEmailVerification = true;
    o.ResetTokenExpiry = TimeSpan.FromHours(1);
});
```

⚠️ **Reset and verification tokens never come back in a response.** They go to
`IPasswordResetNotifier` / `IEmailVerificationNotifier`, whose defaults **only log a warning** and deliver
nothing. Register real ones (a mail from a `.pdxemail` template, see `pragmatic-use-documents`) or users never
receive them. The request operations answer the same whether the email exists or not.

`IPasswordPolicy` replaces the length rule (complexity, history, breach lists); `IPasswordHasher` is
BCrypt by default (`PasswordWorkFactor`, 12).

## The token

`app.UseJwtAuthentication()` reads the `Jwt` section: `Key` (at least 32 bytes, from secrets, never a
committed file), `Issuer`, `Audience`, `TokenExpiration`, `ClockSkew`, `RequireSecurityStamp`, and
registers the issuer `SignInUser` signs with.

What the token says about the user is the application's, through a contributor:

```csharp
public sealed class UserClaims(IReadRepository<AppUser> users) : IUserClaimsContributor
{
    public async ValueTask ContributeAsync(LocalIdentity identity, SignInClaims claims, CancellationToken ct = default)
    {
        // By the account's key: nobody is signed in yet, so the current user is nobody.
        var key = identity.ExternalIdentityKey;
        var user = await users.FirstOrDefaultAsync(Spec<AppUser>.Where(u => u.Identity!.ExternalIdentityKey == key), ct);
        if (user is null) return;
        claims.DisplayName = user.DisplayName;
        claims.Roles.Add(user.Role.ToString());      // roles, not permissions: the host maps them per request
        // claims.Subject = a pseudonym, so ownership and the audit trail never record an email
    }
}

// in the host: every contributor runs, in registration order
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IUserClaimsContributor, UserClaims>());
```

⚠️ **The security stamp revokes tokens.** Every token carries the account's stamp; a password change or
reset rotates it, and on every request the stamp is checked against the store: a stale token is 401.
A token **without** a stamp is refused while `RequireSecurityStamp` is `true` (the default). Turn it off
only while old tokens drain, or on a host whose tokens come from an issuer that owns revocation. Rotate
the stamp yourself whenever what the token claims stops being true (a role changed).

⚠️ Put **roles** in the token, not permissions: permissions are expanded from roles on each request
(`pragmatic-use-authorization`), so changing what a role may do needs no new token.

A module that must sign a token itself depends on `IAccessTokenIssuer`, never on `JwtTokenGenerator`.

## External providers

```csharp
app.UseOidcAuthentication(o =>
{
    o.Authority = cfg["Oidc:Authority"]!;   // discovery document
    o.Audience = cfg["Oidc:Audience"]!;
    o.RoleClaim = "roles";                   // where the provider puts roles (default "roles"); RolePrefix, NameClaim
});

app.UseKeycloakAuthentication(k =>
{
    k.BaseUrl = cfg["Keycloak:BaseUrl"]!;
    k.Realm = "myrealm";
    k.Audience = "my-api";
    k.ClientId = "my-api"; k.ClientSecret = cfg["Keycloak:Secret"];   // enables IKeycloakAdminClient
});
```

Keycloak maps `realm_access` roles and `preferred_username`; with a confidential client it registers
`IKeycloakAdminClient` for provisioning users and synchronising roles. There is no just-in-time
provisioning of a local user on first external sign-in: map the token's subject to your entity with
`[PragmaticUser(MatchClaim = "sub")]`, or create the row in your own first-use operation.

## Outside a request

- `SystemUser` is the `ICurrentUser` for background work: authenticated, full access. A job acting on
  behalf of someone should act **as** them; see `pragmatic-use-delegation`.
- In a message handler there is no request: `MessageContext` carries the originating `UserId` and
  `TenantId`, and the tenant is restored into the consume scope. Read the originator from the context
  when the handler needs it; do not read `HttpContext`.

## Security events on the audit trail

```csharp
builder.Services.AddIdentitySecurityAuditing<TheUserASignInWasAbout>();   // Pragmatic.Identity.Auditing
```

Failed sign-ins and lockouts are recorded on the `Pragmatic.Audit` trail, pseudonymised. Use the generic
overload: `ISecuritySubjectLocator` turns the identity an event named into **your** subject key; the
parameterless overload assumes `("User", email)` and, when your subjects are registered otherwise, writes
every entry with no subject without saying so. The trail (`AddAuditTrail()`) and the subject registry
(`AddSubjectRegistry()`) are registered separately. `UserLoggedIn`, `LoginFailed`, `AccountLocked`,
`PasswordChanged` and the other domain events are there for handlers of your own.

## When every request is 401

- The environment has no scheme (see the startup message) or `Audience`/`Issuer` do not match the token.
- A local token has no stamp, or the password changed since it was issued.
- `ICurrentUser.Id` is empty: the token names its user under another claim; set
  `IdentityOptions.UserIdClaimType` (`services.AddPragmaticIdentity(o => o.UserIdClaimType = "sub")`).
- In tests, a real scheme is on and the test still sends only `X-User-*` headers (or the reverse).

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Time off and Invoicing example
applications (code that compiles and that `TimeOff.IntegrationTests` and `Invoicing.IntegrationTests`
exercise) and kept identical to it by the gate: local accounts imported as a package, the user entity,
what the token says, the host's JWT and security auditing, the subject a failed sign-in was about, the
first account from configuration, and an external OIDC provider.
