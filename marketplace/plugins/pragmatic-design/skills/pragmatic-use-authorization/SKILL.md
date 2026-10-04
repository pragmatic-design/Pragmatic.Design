---
name: pragmatic-use-authorization
description: Use when adding authentication, permissions, roles, JWT, ICurrentUser or per-record access (OwnedEntity, ScopedEntity, DataScopeRule), with Pragmatic.Authorization and Pragmatic.Identity, [RequirePermission], IResourceAuthorizer.
---

# Pragmatic Use Authorization

**Covers:** Authentication, identity, permissions and roles with Pragmatic.Authorization and Pragmatic.Identity from NuGet: ICurrentUser, [RequirePermission], roles and groups, OwnedEntity/ScopedEntity, IResourceAuthorizer, DataScopeRule, JWT.

`Pragmatic.Identity` provides *who the user is* (`ICurrentUser`); `Pragmatic.Authorization` provides *what they can do* (permissions, roles, scopes). Permissions are known at compile-time: the source generator produces constants and a registry, with no reflection-based lookup.

## When to use

- You are protecting actions/endpoints with permissions.
- You are defining roles, groups, or authentication (JWT or external).
- You are restricting data visibility per user/team (`[HasOwner]`, `[HasAccessScopes]`, `DataScopeRule`).
- You are reading the current user inside a mutation/action.

For the `[HasOwner]`/`[HasAccessScopes]` attributes on entities: also see `pragmatic-use-persistence`.

## Packages

```xml
<PackageReference Include="Pragmatic.Authorization" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Identity" Version="1.0.0-alpha.1" />
<PackageReference Include="Pragmatic.Identity.AspNetCore" Version="1.0.0-alpha.1" />     <!-- HTTP bridge -->
<PackageReference Include="Pragmatic.Identity.Local.Jwt" Version="1.0.0-alpha.1" />      <!-- local JWT auth -->
<PackageReference Include="Pragmatic.Identity.Oidc" Version="1.0.0-alpha.1" />           <!-- external IdP (OIDC) -->
<PackageReference Include="Pragmatic.Identity.Persistence" Version="1.0.0-alpha.1" />    <!-- DB-backed users -->
```

`ICurrentUser`, `[RequirePermission]`, `[PragmaticUser]` are in `Pragmatic.Abstractions`. Always add `Pragmatic.SourceGenerator` as an analyzer.

## ICurrentUser: the current user

Inject `ICurrentUser` (namespace `Pragmatic.Identity`) as an uninitialised private field, like any other dependency of an operation:

```csharp
[DomainAction]
[RequirePermission(SalesPermissions.Order.Create)]   // the gate is declared, not written in Execute
public partial class PlaceOrderAction : DomainAction<Guid>
{
    private ICurrentUser _user = null!;

    public override Task<Result<Guid, IError>> Execute(CancellationToken ct)
    {
        var who = _user.Id;          // "" if anonymous
        var tenant = _user.TenantId;
        // ...
    }
}
```

A property the operation only reads from the caller is `[FromCurrentUser]` rather than an injected `ICurrentUser`; see `pragmatic-use-actions-endpoints`.

`ICurrentUser`: `Id`, `DisplayName`, `IsAuthenticated`, `Kind` (`Anonymous|User|Service|System`), `TenantId`, `Claims` (multi-value), `Delegation`, `Authorization`, `Authentication`.

### Acting on behalf of someone: `ICurrentUser.Delegation`

`null` on almost every session. Set when an agent works for the user who started it, a support
operator for a customer, or a background job for the owner of a row. Two things are worth knowing even
if you never use it:

- **`ICurrentUser.Id` stays the subject**, whoever the work is *for*. Row ownership, ownership and
  scope filters, culture: all of them keep pointing at the right person, so code that never heard of
  delegation keeps behaving.
- **The effective authority is composed**, and every `[RequirePermission]`, permission-based filter
  and `ResourcePolicy` inherits that composition without asking.

→ **`pragmatic-use-delegation`** for the policies, the refusal rules, the claims and the traps. Load it
when a session in your product can act for someone else; skip it otherwise.

`Authorization` (`IUserAuthorization`): `Roles`, `Permissions`, `Groups`, `Scopes`, `HasPermission`, `HasAnyPermission`, `HasAllPermissions`, `IsInRole`, `IsInGroup`, `HasScope`.

## Permissions

### 1. Declaring permissions

The CRUD permissions of every entity are generated. Any other permission is **one line on the assembly**:
its value, what holding it allows, and optionally the group a role screen lists it under:

```csharp
// Infrastructure/Authorization/Permissions.cs
[assembly: Permission("billing.invoice.refund", "Refund a paid invoice", Category = "Billing")]
```

The generator adds a `const` to the **same** `{Boundary}Permissions` class the CRUD constants are in (a
resource that is an entity takes it into that entity's class), and the entry (name, description,
category) to the permission registry the host's `IPermissionCatalog` lists. Operations and roles name the
constant:

```csharp
[DomainAction]
[RequirePermission(BillingPermissions.Invoice.Refund)]
public partial class RefundInvoiceAction : DomainAction<RefundReceipt> { /* ... */ }
```

A permission only one operation requires can be declared where it is required, with the same effect:
`[RequirePermission("billing.invoice.refund", Description = "Refund a paid invoice")]`. The value is the full
string as written; nothing is derived from the operation.

**Roles and permissions managed at runtime**: `Pragmatic.Authorization.Management`, imported with
`[UsePackage<AuthorizationManagementPackage, TBoundary>]` and exposed action by action with
`[ExposeEndpoint<…>]`: `CreatePermission`, `ListPermissions`, `CreateRole`, `ListRoles`,
`AssignPermissionsToRole`, `AssignRoleToUser`, `RevokeRoleFromUser`, `GetEffectivePermissions`, under
`authorization.permissions.manage`, `authorization.roles.manage`, `authorization.assignments.manage` and
`authorization.view`. A caller bound to a tenant can act only on its own tenant, and not on the global
level either: a role, permission or assignment with no tenant holds in **every** tenant, so only a caller
with no tenant may write one (`TenantBinding`, `403` otherwise).
**The assignments are authority.** The package registers a permission provider
(`ManagedRolePermissionProvider`) that reads its own tables on every resolution: a role assigned with
`AssignRoleToUser` grants the permissions `AssignPermissionsToRole` gave it, in the request's tenant or
globally, from `ValidFrom` to `ValidTo`. Assign, revoke and a change to a role's permissions evict the
affected users from the permission cache, so the next request sees it. These add to the roles the
identity carries (claims, `IRolePermissionStore`); they do not replace them.

**A tenant admin grants only what they hold.** `AssignPermissionsToRole` refuses (403) a permission the
caller does not hold (a wildcard they hold covers what it names), so `authorization.roles.manage` and
`authorization.assignments.manage` together cannot raise a tenant admin above their own permissions. A
caller with no tenant, the platform operator, is not bound by this.

**What was created at runtime is listed.** The package registers `EfDynamicRoleStore` and
`EfDynamicPermissionStore`; `IPermissionCatalog` merges them with the compiled catalog, so `ListRoles` and
`ListPermissions` show the caller's tenant and the global level. The catalog is **scoped**: resolve it
from a scope, never from the root provider.

**Either of two audiences** (billing reads an invoice, and so does the front desk at checkout with its
own reservation permission) is `[RequireAnyPermission(BillingPermissions.Invoice.Read,
BookingPermissions.Reservation.Read)]`: an OR on one route, instead of a second endpoint returning the
same DTO. It is not a widening: a caller holding neither is refused; test that case too.

`[RequirePermission]` (class target) applies to domain actions, mutations, and endpoints. Name convention: `{boundary}.{entity}.{verb}`; a declared permission whose first segment is not one of the assembly's boundaries is `PRAG1004`; a literal written straight into `[RequirePermission]` is not checked, and wildcard grants match on those segments.

#### A permission on a nested child

A child mutation written through its parent has no endpoint of its own, so the parent is the only way in, and its `[RequirePermission]` is demanded there too. The caller needs both.

That is the default because it is the safe one. Where settling the children **is** the parent's operation, say so:

```csharp
[Mutation(Mode = MutationMode.Update)]
[RequirePermission(Permissions.Term.Update)]
[AbsorbsChildPermissions]                      // curating the set is curating the term
public partial class CurateTermMentionsMutation : Mutation<Term> { /* ... */ }
```

`[AbsorbsChildPermissions]` must be declared: silence means the child's permission is still asked. The parent's own permission is never absorbed by anything.

⚠️ **The entity segment is kebab-case**: `WorkItem` becomes `worksites.work-item.read`. See the note
below on permission values: a role granted against any other spelling matches nothing, silently, so a
permission that "does not work" is usually a permission that was never spelled the same way twice.

⚠️ **A generated endpoint is not permission-gated unless you say so.** Mutations, queries and
endpoints without an explicit `[RequirePermission]` inherit only the root group's
`RequireAuthorizationByDefault`, i.e. *authenticated*, not *authorised*: any signed-in caller can
invoke them. The CRUD constants the generator emits are names to use, not gates it applies. (The
exception is `[Autocomplete]`, whose derived read permission **is** enforced.) To make the root ask for
more than a signed-in caller, set `PragmaticEndpointsOptions.DefaultAuthorizationPolicy` to a policy
registered with `AddAuthorization`: the root then applies `RequireAuthorization(policy)`.

There is no other way to declare a permission: there is no `IPermission` type and no
`[ExplicitPermission<TPermission>]`. `[ExplicitPermission(BillingPermissions.Invoice.Refund)]` takes the constant. In an
assembly with no boundary (a package) the first segment names the class (`authorization.view` →
`AuthorizationPermissions.View`). The constants are what you use, never scattered strings.

⚠️ **The generator already emits the CRUD constants for every entity** (`BillingPermissions.Invoice.Read`, `.Create`, `.Update`, `.Delete`, `.ViewAll`, `.All`). Declaring `billing.invoice.read` again (on the assembly or in a `[RequirePermission(..., Description = ...)]`) is **`PRAG1001`**, and so is declaring one value twice; a value whose constant would take a name the class already uses (`billing.invoice` beside the entity `Invoice`'s class) is **`PRAG1005`**. Pass the generated constant instead of a string.

⚠️ **A permission value is kebab-case, on both segments.** `RoomType` in the `Catalog` boundary gives
`catalog.room-type.read`, not `catalog.roomtype.read`; the trait permissions on it are
`catalog.room-type.comments.read`, and its row-level bypass is `catalog.room-type.view-all`. A role map
or a seeded database carrying the flat form grants nothing, and the two spellings agree on a
single-word name, which is what hides the mistake.

### 2. Wildcards

Permissions support segment wildcards: `*` (everything), `billing.*` (entire boundary), `billing.invoice.*` (all operations on the entity), `billing.*.read` (read everywhere in the boundary).

### 3. Roles and groups

A role is a `partial` class with three attributes; the generator writes its `IRole` members:

```csharp
[Role("manager", "Decides the leave requests of the teams they manage")]
[IncludesRole<EmployeeRole>]                     // its permissions are included (repeatable)
[Grants(LeavePermissions.LeaveRequest.Decide)]   // the generated constants, or values (repeatable)
public sealed partial class ManagerRole;
```

`DefaultPermissions` comes out flattened through the included roles, de-duplicated and ordered, and the role
registry lists the same set. A hand-written `DefaultPermissions => [.. EmployeeRole.DefaultPermissions, …]`
is catalogued as the union too: the generator follows a spread of another role's list or of a list held in a
field/property; one it cannot follow (a method call) is `PRAG1013`, since the runtime grants what the registry
would not list. `PRAG1006` (not partial), `PRAG1007` (roles include each other), `PRAG1008` (a granted constant
no generator writes), `PRAG1009` (a hand-written role of another assembly cannot be included; declare it with
`[Role]` there).

A list held in **another assembly** has no initializer the build can follow, so the assembly that declares it
publishes it: `[PermissionSet]` on the member, and the generator writes its values into that assembly's
metadata. Without it the role is `PRAG1015`: the registry cannot say what it grants, and saying "nothing" is
what a role that grants nothing says.

A user entity that keeps its access level in an enum maps it to a role on the members, never a hand-written
switch:

```csharp
public enum AccessRole
{
    [SignsInAs<EmployeeRole>] Employee,
    [SignsInAs<ManagerRole>] Manager,
}
claims.Roles.Add(employee.Role.RoleNameOf());   // generated: AccessRoleNames.RoleNameOf, one arm per member
```

Once one member has it, every member must: one without is `PRAG1014` (an error), so no declared value signs in
with no role.

Configure them in `Program.cs`:

```csharp
using Pragmatic.Authorization;   // ⚠ without it the compiler picks ASP.NET's parameterless
                                 //   UseAuthorization() and reports CS1501

app.UseAuthorization(authz =>
{
    authz.MapRole("billing-manager", role => role
        .WithAllPermissions<BillingBoundary>()
        .WithoutPermissions("billing.invoice.delete"));

    authz.MapRole<SupportRole>();                       // IRole with DefaultPermissions

    authz.MapGroup("finance-team", g => g.WithRoles("billing-manager", "auditor"));

    authz.UsePermissionCache(TimeSpan.FromMinutes(5));   // caches the resolved set; see the note below
});
```

⚠️ **`MapRole` feeds `IPermissionChecker`, not the HTTP gate directly.** The gate itself is one shape
whatever a boundary references: `[RequirePermission]` puts a `PragmaticPermissionRequirement` in the
endpoint's policy, and that type ships with `Pragmatic.Endpoints.AspNetCore`, which every boundary
declaring an `[Endpoint]` already has. Roles and wildcards expand because the **handler** asks the
checker, and the handler is the host's half (`Pragmatic.Identity.AspNetCore`). A boundary library does
**not** need to reference it. `UsePermissionCache` caches the resolved set; it does not add role
expansion where there is none.

`RoleBuilder`: `WithPermissions(...)`, `IncludeDefinition<TDefinition>()` (module `IRoleDefinition` template), `WithAllPermissions<TBoundary>()`, `WithOperation<TBoundary>(CrudOperation)`, `WithoutPermissions(...)` (not combinable with already-added wildcards), `ClearDefaults()`. `SeedFromJson()` loads roles/groups from `roles.pragmatic.json` (extension generated by the SG when the file exists). A file that is not JSON is `PRAG1010` and seeds nothing; a misspelt property, a wrong kind, an `inherits` naming a role outside the file or a cycle is `PRAG1011`; a second file is `PRAG1012`.

## Authentication

```csharp
app.UseJwtAuthentication();   // the Jwt section: Key (min 32 bytes), Issuer (required outside Development), Audience

app.UseJwtAuthentication(jwt =>                          // or in code, when the values are not configuration
{
    jwt.SigningKey = secrets.JwtKey;
    jwt.Issuer = "myapp";
    jwt.Audience = "myapp-api";
    jwt.TokenExpiration = TimeSpan.FromHours(1);
});
```

Do not map `Jwt:*` into the lambda by hand: the parameterless form does it, and names the key that is
missing. It also reads `TokenExpiration`, `ClockSkew` (TimeSpan) and `RequireSecurityStamp`;
`UseJwtAuthentication("Auth:Tokens")` reads another section.

`UseJwtAuthentication` (from `Pragmatic.Identity.Local.Jwt`) for local token auth; `UseOidcAuthentication` (from `Pragmatic.Identity.Oidc`) when an external provider issues the tokens; `UseAuthentication(...)` / `UseAuthentication<THandler>(scheme)` for a custom handler or scheme. Each of them registers the scheme **and** the pipeline step that adds the authentication/authorization middleware, so you never write `UseAuthentication()`/`UseAuthorization()` yourself. Endpoints require authentication by default; opt out with `[AllowAnonymous]`.

### External provider (OIDC)

```csharp
app.UseOidcAuthentication(o =>
{
    o.Authority = "https://idp.example.com/realms/myrealm";  // its discovery document supplies the keys
    o.Audience  = "my-api";
    o.RoleClaim = "roles";        // default "roles"; a single role or a JSON array both work
    o.NameClaim = "name";         // default "name", what ICurrentUser.DisplayName reads
});
```

The provider's role claim is translated into `ClaimTypes.Role` by `OidcRoleClaimsTransformer`, so the
permissions above work unchanged: the roles you `MapRole<T>()` are matched against what the IdP sent.
`AllowedRoles` (an allow-list) and `RolePrefix` narrow that translation when the IdP is shared with
other applications; a role must satisfy **both** when both are set.

⚠️ **`Audience` is mandatory outside Development**, and `UseOidcAuthentication` throws at startup
without it. It is not a formality: `ValidateAudience` is
tied to whether `Audience` is set, so leaving it empty accepts a token minted for *any* audience of
that same IdP: another client's token opens your API. `RequireHttpsMetadata` stays `true` for the
same reason; lower it only against a local provider.

⚠️ **`JwtOptions.RequireSecurityStamp` defaults to `true`**: fail-closed. A token without the `sstamp` claim is rejected with a bare 401 whose reason appears only in the server log, and a token with one is checked against `ILocalIdentityStore`, which must be registered. The package's `SignInUser` writes the stamp into every token it issues (through `IAccessTokenIssuer`, which `UseJwtAuthentication` registers); a token minted by hand after `LoginUser` passes only if it is given one: `Generate(loginResult, …, securityStamp: identity.SecurityStamp)`. `RequireSecurityStamp = false` is right in two cases only: a migration window for tokens minted before stamps existed, and a host whose tokens come from an issuer that holds the credentials and owns their revocation (the token's lifetime is then the revocation bound). See Identity's getting-started, *The security stamp*.

## Instance-level authorization (ABAC)

When base permission is not enough, because the decision depends on the specific instance:

```csharp
public sealed class InvoiceAuthorizer : IResourceAuthorizer<RefundInvoiceAction>
{
    public ValueTask<bool> CanAccessAsync(ICurrentUser user, RefundInvoiceAction action,
        string operation, CancellationToken ct = default)
        => ValueTask.FromResult(user.Authorization.HasPermission("billing.*")
            || action.Department == user.GetClaim("dept"));
}
```

Register with `authz.AddResourceAuthorizer<InvoiceAuthorizer>()`. The `ResourceAuthorizationFilter` runs at Order 250, after the base permission check.

## Data visibility (row-level)

### OwnedEntity / ScopedEntity

```csharp
[Entity] [HasOwner]   public partial class PrivateNote;   // OwnerId auto-set, filter Priority 200
[Entity] [HasAccessScopes]  public partial class TeamDoc;       // AccessScopes JSON, filter Priority 250
```

`[HasOwner]` binds the entity to `ICurrentUser.Id` at create time; `[HasAccessScopes]` binds it to a set of scopes. Both are written at `SaveChanges` by an interceptor (`OwnershipInterceptor`, `ScopeInterceptor`), so every write path is covered and not only the one going through a mutation: an inserted row with no scopes gets `user:{id}`, a row that already carries scopes keeps them, an update is never re-stamped, and with no current user (a job, a bus message, a seed) nothing is stamped and the row is the system's. Filters are global; bypass via permission `{boundary}.{entity-kebab}.view-all`. Combining both attributes causes a single `DataAccessFilter` to apply OR logic. User scopes are produced by `IUserScopeResolver` (default: `user:{id}`, `role:{r}`, `scope:{claim}`).

### DataScopeRule: dynamic scopes via specification

```csharp
public sealed class EurInvoiceScopeRule : DataScopeRule<Invoice>
{
    public override string ScopeName => "billing-eu";
    public override ScopeStrategy Strategy => ScopeStrategy.Materialized;   // Materialized|Computed|Hybrid
    public override Expression<Func<Invoice, bool>> ToExpression() => i => i.Currency == "EUR";
}
```

Register in an `IStartupStep`: `services.AddDataScopeRule<EurInvoiceScopeRule, Invoice>()`, and that is all; do **not** also register `IScopeMaterializer`, which the generated query-filter registration does. `Materialized` writes the scope into `AccessScopes` at `SaveChanges`, on inserts **and updates** (the rule follows the data, so a row that stops matching loses the scope); `Computed` evaluates the expression at query time instead.

⚠️ The creator's stamp runs **before** the rules, and only when `AccessScopes` is empty, so a row carries both `user:{creator}` and every `scope:{name}` that matches it.

## Database-backed identity

`[PragmaticUser]` on an entity links a domain user to an identity. With `Pragmatic.Identity.Persistence` the SG generates a resolver and profile; `Pragmatic.Identity.Local` provides `LocalIdentity` (password, email, lockout) and the `SignInUser` (credentials → `AccessToken`, claims from each `IUserClaimsContributor`)/`LoginUser`/`RegisterUser`/`ChangePassword` actions. `[ProfileProperty]` properties are aggregated into `IUserProfile`; two names are well-known and wired: `PreferredCulture` (the generated per-user culture provider, on with `UseUserCulture()`; see `pragmatic-use-i18n`) and `TimeZone`. There is no JIT provisioning: nothing creates the local user on a first login from an external IdP; correlate on `Authentication.ExternalIdentityKey` yourself. `ILocalIdentityStore` is generated (`{User}.LocalIdentityStore`, registered) when the user owns a `LocalIdentity` property; do not write one. Self-registration (`RegisterUser`) needs the entity to implement `ISelfRegisteringUser<TUser>` (`static TUser Register(LocalIdentity identity)`); without it the store refuses, and users are provisioned by the application. Emails reach the store normalized (`LocalIdentity.NormalizeEmail`).

The resolver (`{User}Resolver(IReadRepository<TUser>, ICurrentUser)`, `ResolveAsync(ct)`) is what a
query reads with **`[FromCurrentUser(nameof(Employee.Id))]`**: the query's invoker constructs it, loads
the caller's entity after validation and the permission check, and writes that member into the
property (`{ get; private set; }`), which is never a request parameter. `[FromCurrentUser]` without a
member binds `ICurrentUser.Id` to a `string`. That is how a "my …" read is written: not a
`DomainAction` looking the caller up by hand, and not `ICurrentUser` injected into the query, which the
cache key cannot see. **PRAG0730** / **PRAG0731** hold the form; see `pragmatic-use-persistence` →
*Reading by the caller*.

An operation that needs the caller's entity to **decide** something (a mutation checking who approves,
an action exporting the caller's data) injects the resolver: it is registered like a `[Service]` of the
module when the entity is public: `private EmployeeResolver _currentEmployee = null!;` then
`await _currentEmployee.ResolveAsync(ct)`. A sign-in `IUserClaimsContributor` uses
`FindByIdentityKeyAsync(identity.ExternalIdentityKey, ct)`. Never a hand-written lookup on
`ExternalIdentityKey`: that is a second copy of the match the resolver owns.

## Common diagnostics

| ID | Sev | Trigger | Fix |
|---|---|---|---|
| **PRAG0418** | Warning | `[RequirePermission(const)]` unresolvable, so it would **not** be enforced | Use a literal string, or a generated entity permission |
| **PRAG1001** | Error | Duplicate permission name | Rename |
| **PRAG1003** | Error | `IRole.Name` empty or unresolvable | Return a non-empty string literal |
| **PRAG1006** | Error | `[Role]` on a class that is not a top-level `partial` | Make it `partial`, outside any other type |
| **PRAG1007** | Error | Roles include each other | Remove one `[IncludesRole<T>]` from the loop |
| **PRAG1008** | Error | `[Grants(const)]` that no generator writes | Name a generated constant, or write the value |
| **PRAG1009** | Error | `[IncludesRole<T>]` of a hand-written role of another assembly | Declare that role with `[Role]` in its assembly |
| **PRAG1013** | Warning | A spread in a hand-written role's `DefaultPermissions` the generator cannot follow | Spread another role's list or a field/property, or use `[Role]`/`[IncludesRole<T>]` |
| **PRAG1015** | Warning | A role's `DefaultPermissions` reads a list of another assembly that does not publish it | Mark the list `[PermissionSet]` where it is declared, or move it into this assembly |
| **PRAG1016** | Warning | A `[PermissionSet]` list the generator cannot read: nothing is published for it | Write it as a collection expression or an array initializer |
| **PRAG1014** | Error | A member of a `[SignsInAs]` enum without the role it signs in as | Add `[SignsInAs<TRole>]` to the member |
| **PRAG1100** | Error | `[HasOwner]` not `partial` | Add `partial` |
| **PRAG1695** | Error | `Pragmatic.Authorization` without `Pragmatic.Identity`, in a host that does not declare `[AnonymousHost]` (fires in the **host** project; `Pragmatic.Actions` pulls Authorization in transitively) | Add `Pragmatic.Identity.AspNetCore`, or, for an application that deliberately has no authentication, see below |

**An application with no authentication at all.** Declare it on the host module:

```csharp
[Module]
[AnonymousHost]
[Include<CatalogModule, AppDatabase>]
public sealed class HostModule;
```

The generator reads the declaration at compile time: the generated endpoint root carries no
`RequireAuthorization()`, and PRAG1695 is not reported. There is no runtime option to set and no
`NoWarn`. Only the root default is dropped. Authorization that an endpoint or a group declares for
itself is generated as before, and still needs something that authenticates.

**A host with authentication whose endpoints should not require it by default** uses the runtime
option instead. Register the instance in the builder's container. The generated root reads whatever
`PragmaticEndpointsOptions` the container holds, and **the defaults when it holds none**, so leaving it
out means `RequireAuthorizationByDefault = true`, not "off".

```csharp
using Pragmatic.Endpoints.Configuration;

await PragmaticApp.RunAsync(args, app =>
{
    app.Services.AddSingleton(new PragmaticEndpointsOptions { RequireAuthorizationByDefault = false });
});
```

The `{boundary}.{entity}.{verb}` naming matters because wildcard grants (`booking.*`) match on those
segments. A **declared** permission is checked (`PRAG1004` when its first segment names no boundary of
the assembly); a literal written straight into `[RequirePermission]` is not.

## Troubleshooting

**Unexpected 403.** The body names what was missing: `requiredPermissions` (always an array) and `permissionMatch` (`all`/`any`), the same from the HTTP policy and from the action pipeline. Does that permission exist in the user's `DefaultPermissions`/`MapRole`? Check wildcards and `view-all`.

**`ICurrentUser` always anonymous.** Is `Pragmatic.Identity.AspNetCore` referenced and an authentication method (`UseJwtAuthentication`/`UseAuthentication`) configured in `Program.cs`?

**Records visible despite `[HasOwner]`.** Does the user have the `view-all` permission? Is the read running with `FilterMode.Admin` or `Raw`? The filter is applied by the repository's query pipeline, so a read that bypasses the repository does not see it.

## Build verification

```powershell
dotnet build                                              # PRAG10xx = permission/identity issues
dotnet test path\to\App.Tests --no-restore -v minimal
```

## Examples

Complete files in [`examples/`](examples/README.md), copied from the Time off example application (code
that compiles and that `TimeOff.IntegrationTests` exercises) and kept identical to it by the gate: the
permissions that are not CRUD, three roles that include one another and use the generated wildcards, the
scope a row carries for its team's manager, the entity that is stamped with it, and an operation whose
permission and row filter answer two different questions.
