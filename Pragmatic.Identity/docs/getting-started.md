# Getting Started

Step-by-step guide to setting up authentication and identity in a Pragmatic.Design application, from zero to working auth in both development and production.

## Prerequisites

- .NET 10 SDK
- A Pragmatic.Design host application (created via `PragmaticApp.RunAsync`)
- The Pragmatic.Authorization package (auto-detected by the source generator)

## Step 1: Choose Your Authentication Strategy

Pragmatic.Identity supports a config-driven approach where the same codebase can run with development headers or production JWT, controlled by configuration.

### Option A: Development Only (fastest to start)

Add the `Pragmatic.Identity.AspNetCore` package reference to your host project:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/Pragmatic.Identity.AspNetCore.csproj" />
</ItemGroup>
```

Configure `NoOpAuthenticationHandler` in `Program.cs`:

```csharp
using Pragmatic.Identity;
using Pragmatic.Composition.Hosting;

await PragmaticApp.RunAsync(args, app =>
{
    app.UseAuthentication<NoOpAuthenticationHandler>("PragmaticDefault");
});
```

This sets up header-based development auth. See [Dev Authentication](dev-authentication.md) for header details.

### Option B: JWT Authentication (development + production)

Add the `Pragmatic.Identity.Local.Jwt` package (which transitively includes `Identity.Local`, `Identity.AspNetCore`, and `Identity`):

```xml
<ItemGroup>
  <ProjectReference Include="path/to/Pragmatic.Identity.Local.Jwt.csproj" />
</ItemGroup>
```

Configure JWT in `Program.cs`:

```csharp
using Pragmatic.Identity;
using Pragmatic.Identity.Local.Jwt;
using Pragmatic.Composition.Hosting;

await PragmaticApp.RunAsync(args, app =>
{
    if (!string.IsNullOrEmpty(app.Configuration["Jwt:Key"]))
    {
        // The Jwt section: Key, Issuer, Audience (and TokenExpiration, ClockSkew, RequireSecurityStamp)
        app.UseJwtAuthentication();
    }
    else
    {
        // Fallback to dev auth when no JWT key is configured
        app.UseAuthentication<NoOpAuthenticationHandler>("PragmaticDefault");
    }
});
```

Add the JWT configuration to `appsettings.json` (production) or `appsettings.Development.json`:

```json
{
  "Jwt": {
    "Key": "your-256-bit-secret-key-at-least-32-characters-long",
    "Issuer": "https://myapp.example.com",
    "Audience": "https://myapp.example.com",
    "ExpirationHours": 1
  }
}
```

### Option C: External Identity Provider (Keycloak, Auth0, Entra ID)

Use the standard ASP.NET Core authentication builder:

```csharp
using Pragmatic.Identity;
using Pragmatic.Composition.Hosting;

await PragmaticApp.RunAsync(args, app =>
{
    app.UseAuthentication(auth =>
    {
        auth.AddOpenIdConnect("oidc", options =>
        {
            options.Authority = "https://your-idp.example.com";
            options.ClientId = "your-client-id";
            options.ClientSecret = "your-client-secret";
            // ...
        });
    });
});
```

## Step 2: Configure Authorization

Authorization is configured alongside authentication. The `UseAuthorization()` extension on `IPragmaticBuilder` lets you define roles, groups, and policies.

```csharp
using Pragmatic.Authorization;
using Pragmatic.Authorization.Configuration;

await PragmaticApp.RunAsync(args, app =>
{
    // ... authentication setup from Step 1 ...

    app.UseAuthorization(authz =>
    {
        // Default policy: require authentication for all endpoints
        // Endpoints require authentication by default (PragmaticEndpointsOptions.RequireAuthorizationByDefault);
        // opt a single endpoint out with [AllowAnonymous].

        // Define roles with their permissions
        authz.MapRole<AdminRole>();

        authz.MapRole<ManagerRole>(r => r
            .WithPermissions("orders.read", "orders.create", "orders.update")
            .IncludeDefinition<CatalogReader>());

        // Define groups that aggregate roles
        authz.MapGroup<CustomerCareGroup>();

        // Optional: cache resolved permissions across requests
        authz.UsePermissionCache(TimeSpan.FromMinutes(5));
    });
});
```

### Define Roles

Roles implement `IRole` and optionally `IRoleDefinition` for reusable permission sets:

```csharp
// A simple role with wildcard access
public sealed class AdminRole : IRole
{
    public static string Name => "admin";
    public static string? Description => "Full access administrator";
    public static IReadOnlyList<string> DefaultPermissions => ["*"];
}

// A role definition for reuse across application roles
public sealed class CatalogReader : IRoleDefinition
{
    public static string Name => "catalog-reader";
    public static string? Description => "Read-only access to catalog";
    public static IReadOnlyList<string> Permissions =>
    [
        "catalog.amenity.read",
        "catalog.property.read"
    ];
}
```

### Define Groups

Groups aggregate multiple roles:

```csharp
public sealed class CustomerCareGroup : IGroup
{
    public static string Name => "customer-care";
    public static string? Description => "Customer care team";
    public static IReadOnlyList<string> DefaultRoles => ["booking-manager", "catalog-viewer"];
}
```

## Step 3: Access the Current User

Inject `ICurrentUser` anywhere in your application to access the authenticated user's identity:

```csharp
public class OrderService(ICurrentUser currentUser)
{
    public async Task CreateOrder(CreateOrderRequest request)
    {
        // Check authentication
        if (!currentUser.IsAuthenticated)
            throw new UnauthorizedException();

        // Read user identity
        var userId = currentUser.Id;
        var tenantId = currentUser.TenantId;

        // Check permissions
        if (!currentUser.Authorization.HasPermission("orders.create"))
            throw new ForbiddenException();

        // Check roles
        if (currentUser.Authorization.IsInRole("admin"))
            // admin-specific logic

        // Access authentication metadata
        var issuer = currentUser.Authentication.Issuer;
        var isMfa = currentUser.Authentication.IsMfaAuthenticated;
    }
}
```

### In Domain Actions

Domain actions receive `ICurrentUser` as a dependency (auto-injected by the source generator):

```csharp
[DomainAction]
[RequirePermission("orders.create")]
public partial class CreateOrder : DomainAction<OrderResult>
{
    private ICurrentUser _currentUser = null!;

    public override async Task<Result<OrderResult, IError>> Execute(CancellationToken ct = default)
    {
        // _currentUser is available here, injected by the SG
        var createdBy = _currentUser.Id;
        // ...
    }
}
```

## Step 4: Add Local Credential Management (Optional)

If you want self-hosted user registration, login, and password management (instead of or alongside an external IdP), add `Pragmatic.Identity.Local`:

```csharp
// The Local package provides these domain actions:
// - RegisterUser            → creates a new local identity with email + hashed password
// - LoginUser               → authenticates with email + password, returns LoginResult
// - SignInUser              → the same check, then the access token (needs Pragmatic.Identity.Local.Jwt)
// - ChangePassword          → changes password for the current user (requires the ChangePassword permission)
// - RequestPasswordReset    → issues a reset token, delivered via notifier (does not reveal email existence)
// - ConfirmPasswordReset    → validates token and sets new password
// - RequestEmailVerification → issues an email-verification token via notifier
// - ConfirmEmail            → validates token and marks the email verified
```

These actions are exposed as endpoints by the source generator when the host references the `Pragmatic.Identity.Local` package. The route prefix defaults to `identity/local`.

> **Register a notifier for reset / verification tokens.** `RequestPasswordReset` and
> `RequestEmailVerification` never return the token in the response — they hand the plaintext to
> `IPasswordResetNotifier` / `IEmailVerificationNotifier` for out-of-band delivery. The defaults
> (`LogOnlyPasswordResetNotifier`, `LogOnlyEmailVerificationNotifier`) **do not deliver anything** and
> only log a warning. Register a real email/SMS-backed implementation, otherwise users never receive
> their tokens:
>
> ```csharp
> services.AddScoped<IPasswordResetNotifier, EmailPasswordResetNotifier>();
> services.AddScoped<IEmailVerificationNotifier, EmailVerificationNotifier>();
> ```

### The identity store is generated

When the `[PragmaticUser]` entity owns a `LocalIdentity` — a property of that type, stored in the user's
row — and the project references `Pragmatic.Persistence.EFCore`, the generator writes
`{User}.LocalIdentityStore` and registers it as `ILocalIdentityStore`. Its finders load the **user**,
tracked, and hand back its identity, so `UpdateAsync` saves what the actions changed; every save goes
through the boundary's unit of work. The emails it receives are already in their stored form
(`LocalIdentity.NormalizeEmail`): the actions normalize before they ask.

Self-registration (`RegisterUser`) has to create a user, and what else a new user needs is the
application's to say. The entity says it by implementing `ISelfRegisteringUser<TUser>`:

```csharp
[Entity]
[PragmaticUser(MatchClaim = "sub")]
public partial class AppUser : IEntity, ISelfRegisteringUser<AppUser>
{
    public string? DisplayName { get; set; }
    public LocalIdentity? Identity { get; set; }

    public static AppUser Register(LocalIdentity identity)
    {
        var user = Create();
        user.DisplayName = identity.Email;
        user.Identity = identity;
        return user;
    }
}
```

Without it the store's `CreateAsync` throws `NotSupportedException`: the application provisions its
users itself (an administrator creates them), as Time off does.

A project that declares its own class implementing `ILocalIdentityStore` keeps it, and nothing is
generated.

## Step 5: Sign In with a Token (Optional)

With `Pragmatic.Identity.Local.Jwt`, the package's `SignInUser` checks the credentials exactly as
`LoginUser` does and returns the token. `UseJwtAuthentication` registers the `IAccessTokenIssuer` it signs
with, and the token always carries the account's key and its **security stamp** — without the stamp,
`UseJwtAuthentication` refuses a token by default (see [The security stamp](#the-security-stamp) below).
Expose it from the module that imports the package:

```csharp
[UsePackage<LocalIdentityPackage, AppBoundary>]
[ExposeEndpoint<SignInUser>(HttpVerb.Post, "sign-in", AllowAnonymous = true)]   // POST identity/local/sign-in
public sealed class AppModule;
```

The answer is an `AccessToken`: `{ "token": "…", "expiresAt": "…" }`.

What the token says about the user beyond the account — a display name, roles, a tenant, or a subject
other than the account's key — is the application's to say, with an `IUserClaimsContributor`:

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
        claims.TenantId = profile.TenantId;
        foreach (var role in profile.Roles)
            claims.Roles.Add(role);
    }
}

// In the host: every contributor registered runs, in registration order.
services.TryAddEnumerable(ServiceDescriptor.Scoped<IUserClaimsContributor, UserProfileClaims>());
```

A contributor adds to the token; the account's key and the stamp are not its to change. A module that
has to sign a token itself depends on `IAccessTokenIssuer`, never on `JwtTokenGenerator`: the contract is
`[ProvidedByHost]`, so the module's generator does not report it as an unregistered dependency.

### The security stamp

`LocalIdentity.SecurityStamp` is an opaque value the token carries as its `sstamp` claim. A password change
or reset rotates it, and every token issued before the rotation stops being accepted. It is how a local
sign-in revokes the sessions of a user whose password was compromised.

What `UseJwtAuthentication` does with it, on every request:

- **A token with a stamp** is checked against the store. The identity is loaded from `ILocalIdentityStore`
  by the external identity key the token carries (its `pragmatic_eid` claim, else its `sub`), and the token
  is refused unless the identity is active and its current stamp equals the token's. So an
  `ILocalIdentityStore` has to be registered; without one, a stamped token is refused.
- **The subject can be a pseudonym.** `Generate(subject, …, externalIdentityKey: …)` writes the two
  separately: the account is found by the key, and `ICurrentUser.Id` — what ownership stamps and the audit
  trail record as the actor — is the subject. Pass a reference rather than the key, and nothing that
  records who acted records an email.
- **A token without a stamp** is refused while `JwtOptions.RequireSecurityStamp` is `true`, which is the
  default. A token that carries no stamp cannot be revoked, and would outlive the password change meant to
  end it. This is the 401 a token minted without `securityStamp:` gets on every request.

`RequireSecurityStamp = false` is right in two cases, and only these:

1. **A migration window**: tokens minted before stamps existed are still in circulation and must keep
   working until they expire. Turn it back on once every live token carries a stamp.
2. **A host whose tokens come from an issuer that holds the credentials and owns their revocation**: an
   external identity provider, or a separate token service. This host stores no password, so it has no
   stamp to rotate and no store to check one against. Revoking a session is the issuer's job, and the
   token's lifetime is the bound on how long a revoked session keeps working. Keep that lifetime short.

```json
// Case 2: the tokens are issued elsewhere, and revocation is the issuer's.
"Jwt": { "Key": "…", "Issuer": "…", "Audience": "…", "RequireSecurityStamp": false }
```

`app.UseJwtAuthentication()` reads it; in code, `jwt.RequireSecurityStamp = false` in the lambda overload.

## Step 6: Add Database-Backed Authorization (Optional)

For production scenarios where role-permission mappings need to be managed at runtime (not just at compile time), add `Pragmatic.Identity.Persistence`. See [Persistence](persistence.md) for full details.

## Verification Checklist

After completing setup, verify:

- [ ] `ICurrentUser` resolves correctly in DI (inject into a controller/action and check `Id`, `IsAuthenticated`)
- [ ] In development: `X-User-Id` header creates an authenticated identity
- [ ] In development: requests without `X-User-Id` result in `AnonymousUser` (or 401 if policy requires auth)
- [ ] In production: JWT tokens validate correctly and populate `ICurrentUser`
- [ ] Permission checks work: `[RequirePermission("...")]` on actions enforces access
- [ ] Role mapping works: roles assigned to users expand to correct permissions

## Common Issues

### ICurrentUser.Id is empty

The `UserIdClaimType` does not match the claim in your token. Check `IdentityOptions`:

```csharp
services.AddPragmaticIdentity(opts =>
{
    opts.UserIdClaimType = "sub"; // Match your token's claim type
});
```

### Permissions always denied

1. Verify role-to-permission mappings are registered via `MapRole<T>()`
2. Check that the user's token/headers include the correct role claims
3. Ensure `AddPragmaticAuthorization()` is called (happens automatically with `UseAuthentication` or `UseJwtAuthentication`)

### HeaderUserMiddleware not working

The middleware must run **before** `UseAuthentication()`. You must register it manually in your `IStartupStep.ConfigurePipeline` (e.g., `app.UseMiddleware<Pragmatic.Identity.HeaderUserMiddleware>();`).

### Circular dependency with IUserAuthorization

`ClaimsPrincipalUserAccessor` resolves `IUserAuthorization` lazily via `IServiceProvider` to break the circular dependency: `ICurrentUser -> CachedPermissionResolver -> ICurrentUser`. This is handled internally; no user action is needed.
