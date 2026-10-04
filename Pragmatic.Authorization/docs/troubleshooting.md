# Troubleshooting

Practical problem/solution guide for Pragmatic.Authorization. Each section covers a common issue, the likely causes, and the fix.

---

## User Gets 403 Forbidden on Every Request

Authenticated users are denied access to all endpoints, even those they should have permission for.

### Checklist

1. **Is `UseAuthorization` called in `Program.cs`?** Without it, no role-to-permission mappings exist. Users with `role` claims have no mechanism to resolve those roles into permissions.

2. **Are roles mapped correctly?** Check that the role names in `MapRole` match the `role` claim values in the user's token. Role names are case-insensitive.

   ```csharp
   // If the JWT has role claim "booking-manager":
   authz.MapRole<BookingManagerRole>();  // BookingManagerRole.Name must return "booking-manager"
   ```

3. **Does the role have the right permissions?** Check `IRole.DefaultPermissions` and the `IncludeDefinition` / `WithPermissions` calls in the builder:

   ```csharp
   authz.MapRole<BookingManagerRole>(r => r
       .IncludeDefinition<BookingOperator>());  // What permissions does BookingOperator define?
   ```

4. **Is the `[RequirePermission]` string correct?** Compare the permission string on the action with the permissions granted to the role. A typo like `"booking.reservations.read"` (plural) will not match `"booking.reservation.read"` (singular). Use SG-generated constants.

5. **Is auto-derivation on?** With `[assembly: PragmaticAutoDerivePermissions]` or the matching build property, an operation that declares no permission gets one derived from its name: kebab-case, three segments when the name opens with a recognised verb (`AddGuestCommentAction` → `booking.guest-comment.add`), two otherwise (`IssueRefundAction` → `booking.issue-refund`). Read the generated permission requirement registry for what it actually asks, and check a role grants that exact string.

6. **Check the permission claim type.** By default, direct permissions come from the `"permission"` claim. If your token uses a different claim type (e.g., `"permissions"` or `"scp"`), configure `AuthorizationOptions.PermissionClaimType`.

7. **Is `ActionCallContext.IsInternalCall` always false?** Internal calls skip permission and policy checks. If authorization is being enforced on intra-boundary calls, check that `ActionCallContext` is properly scoped.

---

## Permissions Not Resolving from Roles

The user has a `role` claim, but `IUserAuthorization.HasPermission` returns false for permissions that should be in that role.

### Checklist

1. **Is the `RoleExpansionProvider` registered?** It is only registered when `MapRole` is called or `UseRolePermissionStore<T>()` is used. If neither is called, `role` claims are not expanded.

2. **Does the `InMemoryRolePermissionStore` contain the role?** Add a breakpoint or log in `RoleExpansionProvider.ResolvePermissionsAsync` to verify the store returns permissions for the role name.

3. **Wildcard matching order:** If the role grants `"booking.*"`, the user gets this string in their resolved set. `HasPermission("booking.reservation.read")` first checks exact match (no hit for `"booking.*"`), then checks wildcards (hit). This should work. If it does not, check that `WildcardMatcher` is reached (add a breakpoint on `CachedPermissionResolver.MatchesPermission`).

4. **Case mismatch in role name:** Role names are case-insensitive in the `InMemoryRolePermissionStore` (uses `StringComparer.OrdinalIgnoreCase`). But verify your custom store (if used) also handles case-insensitivity.

---

## Permissions Not Resolving from Groups

The user has a `group` claim, but permissions from group membership are not resolved.

### Checklist

1. **Is `MapGroup` called?** The `GroupExpansionProvider` is only registered when `MapGroup` is called or `UseGroupRoleStore<T>()` is used.

2. **Are the group's roles also mapped?** `GroupExpansionProvider` resolves groups to roles (via `IGroupRoleStore`), then roles to permissions (via `IRolePermissionStore`). Both stores must be populated.

   ```csharp
   // Group maps to roles:
   authz.MapGroup<CustomerCareGroup>();  // DefaultRoles = ["booking-manager", "catalog-viewer"]

   // Those roles must also be mapped:
   authz.MapRole<BookingManagerRole>(r => r.IncludeDefinition<BookingOperator>());
   authz.MapRole<CatalogViewerRole>(r => r.IncludeDefinition<CatalogReader>());
   ```

3. **Is the group claim name correct?** The `GroupExpansionProvider` reads from `ICurrentUser.Claims["group"]`. If your token uses a different claim type (e.g., `"groups"`), the provider will not find it. Ensure your authentication handler maps the claim type to `"group"`.

---

## ResourcePolicy Evaluation Returns False Unexpectedly

A `[RequirePolicy<T>]` attribute is present, but the policy always denies.

### Checklist

1. **Does the policy have a parameterless constructor?** `PolicyEvaluationFilter` creates instances via `Activator.CreateInstance`. The `new()` constraint on `RequirePolicyAttribute<T>` catches this at compile time, but verify no runtime factory is bypassing it.

2. **Is the user authenticated?** `PolicyEvaluationFilter` returns `UnauthorizedError` (401) if `CurrentUser.IsAuthenticated` is false, before evaluating the policy. If you see 401 instead of 403, the issue is authentication, not the policy.

3. **Debug the policy directly.** Policies are pure functions on `ICurrentUser`. Unit test them:

   ```csharp
   var policy = new ReservationManagementPolicy();
   var mockUser = CreateMockCurrentUser(permissions: ["booking.reservation.create"]);
   var result = policy.Evaluate(mockUser);
   Assert.True(result);
   ```

4. **Check operator precedence.** `&` (AND) binds tighter than `|` (OR) in C#:

   ```csharp
   // This is (A & B) | C, not A & (B | C)
   var policy = ResourcePolicy.IsAuthenticated()
       & ResourcePolicy.RequirePermission("booking.reservation.create")
       | ResourcePolicy.HasPrincipalKind(PrincipalKind.Service);
   ```

   Use parentheses for clarity.

5. **Internal calls skip policy evaluation.** `PolicyEvaluationFilter` checks `CallContext.IsInternalCall` and skips evaluation for intra-boundary calls. If you expect the policy to run on an internal call, this is by design.

---

## IResourceAuthorizer Not Being Called

The `IResourceAuthorizer<T>` is registered but `CanAccessAsync` never executes.

### Checklist

1. **Is the authorizer registered with the correct type parameter?** The `ResourceAuthorizationFilter` resolves `IResourceAuthorizer<TAction>` where `TAction` is the concrete action type. If you registered `IResourceAuthorizer<BaseAction>` but the action is `RefundInvoiceAction`, the filter will not find it (unless `BaseAction` is `RefundInvoiceAction`).

2. **Was `AddResourceAuthorizer<T>()` called?** Check `Program.cs`. The method discovers all `IResourceAuthorizer<T>` interfaces on the type via reflection and registers each.

3. **Is the action invoked through the pipeline?** `ResourceAuthorizationFilter` runs in the action pipeline (Order 250). If the action is called directly (not through `IDomainActionInvoker`), the filter does not run.

4. **Is the call internal?** `ResourceAuthorizationFilter` does not check `IsInternalCall`, but `PolicyEvaluationFilter` at Order 210 does. Verify which filter is skipping. Resource authorization runs at Order 250 and does not skip internal calls.

---

## SG Not Generating Permission Constants

Expected `BookingPermissions.Reservation.Read` but the constant does not exist after build.

### Checklist

1. **Is `Pragmatic.Authorization` referenced?** The SG generates permission constants only when `FeatureDetector.HasAuthorization` is true. This flag is set when the assembly references `Pragmatic.Authorization`.

2. **Is it a CRUD constant?** Those come from the entities: the entity must belong to a boundary, and the assembly must reference `Pragmatic.Persistence.EFCore`. The class is `{Boundary}Permissions`, in the entities' root namespace (the first two segments of `{App}.{Module}.Entities`).

3. **Is it a declared permission?** It needs an `[assembly: Permission("…", "…")]` line (or a `[RequirePermission("…", Description = "…")]`) and a build without `PRAG1004`, `PRAG1001` or `PRAG1005`: a refused declaration gets no constant.

4. **Check the SG output.** In Visual Studio, expand **Dependencies > Analyzers > Pragmatic.SourceGenerator** in Solution Explorer. The class is in `_Infra.Identity.EntityPermissions.g.cs`.

5. **Is the path the one you wrote?** A declared permission's path is its value's segments in PascalCase: `billing.invoice.refund` is `BillingPermissions.Invoice.Refund`, `leave.personal-data.erase` is `LeavePermissions.PersonalData.Erase`.

---

## Cross-Request Cache Not Working

Permissions are resolved fresh on every request despite `UsePermissionCache` being configured.

### Checklist

1. **Is `ICacheStack` registered?** Cross-request caching requires `Pragmatic.Caching` to be referenced and `ICacheStack` to be available in DI. Without it, `CachedPermissionResolver` falls back to request-scoped caching only.

2. **Is the user authenticated?** `CachedPermissionResolver.CanUseCrossRequestCache()` checks three conditions: `_cache is not null`, `_cacheOptions is not null`, and `_currentUser.IsAuthenticated`. Anonymous users are never cached cross-request.

3. **Check the cache key.** The key is `{prefix}:g:u:{userId}` (global) or `{prefix}:t:{tenantId}:u:{userId}` (multi-tenant). If the user ID changes between requests (e.g., different header values in development), each request gets a different cache key.

4. **Check the expiration.** Default TTL is 5 minutes. If you are testing with short intervals, the cache may have expired.

---

## Diagnostics Reference

| ID | Severity | Cause | Fix |
|----|----------|-------|-----|
| PRAG1001 | Error | A permission value declared twice: two declarations, or a declaration equal to a CRUD permission | Declare each value once; a CRUD permission is already generated |
| PRAG1004 | Error | A declared permission's first segment is none of the assembly's boundaries | Start the value with the boundary it belongs to: `{boundary}.{resource}.{verb}` |
| PRAG1005 | Error | A declared permission's constant would take a name its class already uses: a resource, an entity, another verb | Pick a value whose segments do not reuse, at the same depth, a name the class already has |
| PRAG1003 | Error | `IRole.Name` is empty | Ensure the static abstract `Name` property returns a non-empty string literal |
| PRAG1015 | Warning | A role's `DefaultPermissions` reads a list held in another assembly that does not publish it, so the registry would list an empty grant while the runtime grants every entry | Mark the list `[PermissionSet]` in the assembly that declares it, or move it into this one |
| PRAG1016 | Warning | A `[PermissionSet]` list the generator cannot read: the assembly publishes nothing for it | Write the list as a collection expression or an array initializer, in place or in a member of this compilation |
| PRAG0418 | Warning | `[RequirePermission(constant)]` could not be resolved, so it would **not** be enforced | Use a literal permission string, or a generated entity permission |

Permission naming (`boundary.entity.operation`) is checked only where a permission is declared: the
first segment of an `[assembly: Permission]` must be a boundary (`PRAG1004`). A literal written in
`[RequirePermission]` is not checked. It matters because wildcard grants such as `booking.*` match on those segments.

### Runtime Exceptions

| Exception | Thrown by | Cause | Fix |
|-----------|----------|-------|-----|
| `InvalidOperationException` ("Cannot exclude...covered by wildcard") | `RoleBuilder.Resolve()` | `WithoutPermissions` conflicts with a wildcard grant | Use explicit permissions instead of wildcards when using `WithoutPermissions` |
| `NotSupportedException` ("Custom policies cannot be serialized") | `PolicySerializer.Serialize()` | Attempting to serialize a `Custom()` or `RequireExternalPermission()` policy | Use built-in factory methods for serializable policies |

---

## FAQ

### Can I use multiple `[RequirePermission]` attributes on one action?

No. `[RequirePermission]` has `AllowMultiple = false`. Use `[RequirePermission("perm.a", "perm.b")]` for AND logic (all required), or create a policy with `RequireAnyPermission` for OR logic.

### How do I check permissions in business logic (not just as a gate)?

Use `ICurrentUser.Authorization.HasPermission("permission.name")` for branching logic inside your handler. The `[RequirePermission]` attribute is for the gate; `IUserAuthorization` is for in-handler decisions.

### Can I add permissions at runtime (not from code)?

Yes. Implement `IDynamicPermissionStore` for runtime-created permissions. They are merged with SG-generated permissions in `DefaultPermissionCatalog`. For runtime role management, implement `IDynamicRoleStore`.

### How do I test authorization in integration tests?

Use the `NoOpAuthenticationHandler` with HTTP headers:

```csharp
// Set roles via headers
var client = factory.CreateClient();
client.DefaultRequestHeaders.Add("X-User-Id", "test-user-id");
client.DefaultRequestHeaders.Add("X-User-Roles", "booking-manager");
```

The `NoOpAuthenticationHandler` maps these headers to claims. The `RoleExpansionProvider` resolves the `role` claim to permissions via the in-memory stores configured in `UseAuthorization`.

### Why does `PolicyEvaluationFilter` use reflection (`Activator.CreateInstance`)?

`RequirePolicyAttribute<T>` has a `new()` constraint, so `Activator.CreateInstance` is safe. The created instance is cached in a static `ConcurrentDictionary` and reused for all subsequent requests. This is a one-time cost per policy type.

### How do internal calls (intra-boundary) work with authorization?

`PolicyEvaluationFilter` (Order 210) checks `CallContext.IsInternalCall` and skips evaluation for internal calls. The user context (`ICurrentUser`) is still preserved, so downstream `IResourceAuthorizer` and `IQueryFilter` checks can still access the user's permissions. Only the permission gate and policy evaluation are skipped.

### Can I use `[AllowAnonymous]` to override `[RequirePermission]`?

`[AllowAnonymous]` is an ASP.NET Core concept that overrides endpoint-level authorization. It works with `Pragmatic.Endpoints` group-level authorization. For action-level authorization (`[RequirePermission]` on a `DomainAction`), the enforcement happens in the action pipeline, not in ASP.NET Core middleware. To skip authorization on specific actions, do not apply `[RequirePermission]` to them.

### What happens when a user has both direct permissions and role-based permissions?

All provider results are merged with union semantics (additive). If `ClaimsPermissionProvider` (Order 0) returns `{"booking.reservation.read"}` and `RoleExpansionProvider` (Order 100) returns `{"booking.*"}`, the final set is `{"booking.reservation.read", "booking.*"}`. No provider can remove permissions added by another.

### How do I list all permissions in the system for an admin UI?

Use `IPermissionCatalog.GetAllPermissionsAsync()`. This returns SG-generated permissions merged with any `IDynamicPermissionStore` implementations. For roles, use `IPermissionCatalog.GetAllRolesAsync()`. For protected resources and their policies, use `IResourceCatalog.GetAllResourcesAsync()`.

### Can I serialize a policy to store it in a database?

Yes, use `PolicySerializer.Serialize(policy)` to convert a `ResourcePolicy` to a `PolicyExpression` record tree. The expression can be serialized to JSON. To restore: `PolicySerializer.Deserialize(expr)`. Delegate-based policies (`Custom()` and `RequireExternalPermission()`) are not serializable and throw `NotSupportedException`.

---

## Startup Fails with InvalidOperationException

### WithoutPermissions Wildcard Conflict

**Error:** "Cannot exclude 'booking.reservation.delete' -- covered by wildcard 'booking.*'. Use explicit permissions instead of wildcards when using WithoutPermissions."

**Cause:** `RoleBuilder.WithoutPermissions` detects that the excluded permission is covered by a wildcard grant, which would silently fail at runtime.

**Fix:** Replace the wildcard with explicit permissions, then exclude the unwanted one:

```csharp
// Instead of:
authz.MapRole<Role>(r => r
    .WithPermissions("booking.*")
    .WithoutPermissions("booking.reservation.delete"));

// Use:
authz.MapRole<Role>(r => r
    .WithPermissions(
        BookingPermissions.Reservation.Read,
        BookingPermissions.Reservation.Create,
        BookingPermissions.Reservation.Update,
        BookingPermissions.Guest.All));
```

---

## Observability Counters Show High Denial Rate

If `pragmatic.authorization.permission_denied` is much higher than expected:

### Checklist

1. **Check role mappings.** A common cause is roles that are too restrictive. Verify that each role has the permissions its users need.

2. **Check permission naming.** If the SG generates `booking.reservation.read` but the `[RequirePermission]` uses a hardcoded string with a different casing or spelling, the exact match fails. Even though matching is case-insensitive, use generated constants to be safe.

3. **Check wildcard coverage.** A role with `"booking.reservation.*"` does not grant `"billing.invoice.read"`. Verify that wildcard patterns cover the right boundary and entity.

4. **Check for anonymous requests.** Unauthenticated requests always fail permission checks. For a public endpoint use `[AllowAnonymous]` rather than expecting an anonymous user to hold permissions.

5. **Check cache behavior.** If `pragmatic.authorization.cache_misses` is very high, the provider chain runs on every request. This may indicate that cross-request caching is not configured or that `ICacheStack` is not registered.

---

## JSON Role Seeding Not Working

The `roles.pragmatic.json` file exists but `SeedFromJson()` is not available.

### Checklist

1. **Is the file included as an AdditionalFile?** The SG reads `roles.pragmatic.json` via `AdditionalTextsProvider`. Ensure your `.csproj` includes it:

   ```xml
   <ItemGroup>
     <AdditionalFiles Include="roles.pragmatic.json" />
   </ItemGroup>
   ```

2. **Is the file name exact?** The SG looks for files ending with `roles.pragmatic.json` (case-insensitive).

3. **Is there a `PRAG1010`, `PRAG1011` or `PRAG1012`?** A file that is not JSON (`PRAG1010`) seeds nothing; a property or value the format does not understand (`PRAG1011`) is left out; a second file (`PRAG1012`) is not read. Each diagnostic names the file.

4. **Check the SG output.** Look for a generated file named `_Infra.Identity.RoleSeeding.g.cs` in the analyzer output.

---

## Getting Help

- **GitHub Issues**: [github.com/pragmatic-design/Pragmatic.Design/issues](https://github.com/pragmatic-design/Pragmatic.Design/issues)
- **Showcase Examples**: See the `Showcase` project for working authorization configurations, role compositions, and integration tests.
- **Getting Started**: See [getting-started.md](getting-started.md) for a zero-to-running walkthrough.
- **Permission Resolution**: See [permission-resolution.md](permission-resolution.md) for provider chain internals.
- **Policies**: See [policies.md](policies.md) for composable `ResourcePolicy` and `AsyncResourcePolicy`.
- **Roles and Groups**: See [roles-and-groups.md](roles-and-groups.md) for the composition model.
- **Stores**: See [stores.md](stores.md) for in-memory vs database-backed stores.
