# Pragmatic.Abstractions -- Complete Interface Catalog

This document lists every public type in `Pragmatic.Abstractions` with its full member signatures, organized by domain. For a higher-level overview, see the [README](../README.md).

---

## 1. Result

### `IError` (`Pragmatic.Result`)

Base contract for all error types in the Result pattern. Supports HTTP status code mapping and RFC 7807 Problem Details.

```csharp
public interface IError
{
    string Code { get; }                 // Semantic error code, UPPER_SNAKE_CASE (e.g., "NOT_FOUND")
    int StatusCode { get; }              // HTTP status code
    string Title => string.Empty;        // Problem Details title (default: empty)
    string? Description => null;         // Additional context (default: null)
}
```

**Usage:** Extend the `Error` abstract record in `Pragmatic.Result` for class-based errors. Implement `IError` directly only for struct-based errors (e.g., `ValidationError`). For localized errors, implement `ILocalizedError` which adds `LocalizationKey`.

---

## 2. Persistence -- Entities

### `IEntity` (`Pragmatic.Persistence.Entity`)

An entity: something with an identity that outlives a single request. The key is a `Guid`, always;
there is no type parameter.

```csharp
public interface IEntity
{
    System.Guid PersistenceId { get; }   // assigned by the generated Create(), never by the database
}
```

### `IAuditable` (`Pragmatic.Persistence.Entity`)

Audit tracking for creation and last update.

```csharp
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    string? CreatedBy { get; set; }
    DateTimeOffset? UpdatedAt { get; set; }
    string? UpdatedBy { get; set; }
}
```

The `CreatedBy`/`UpdatedBy` fields are populated automatically by the persistence layer using `ICurrentUser.IdOrNull()`.

### `ISoftDelete` (`Pragmatic.Persistence.Entity`)

Soft-delete support. Entities are not physically removed; instead `IsDeleted` is set to `true`.

```csharp
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
    string? DeletedBy { get; set; }
}
```

The query filters that exclude soft-deleted rows are emitted from the `[SoftDelete]` attribute, not from this interface: an entity that implements it by hand is recognised by the EF Core interceptors and excluded by no filter.

### `IChangeTracking` (`Pragmatic.Persistence.Entity`)

Property-level change tracking, implemented automatically by the source generator on entities with generated `Set{Property}` methods.

```csharp
public interface IChangeTracking
{
    IReadOnlySet<string> ModifiedProperties { get; }     // Modified scalar properties
    IReadOnlySet<string> CollectionsModified { get; }    // Modified collection navigations
    void ResetModifiedProperties();                       // Clear tracking state
    bool IsNew { get; set; }                              // True for unsaved entities (all props "modified")
}
```

**Enables:** selective validation (only modified properties), audit trail, optimized persistence (only changed columns).

### `ITemporalRelation` (`Pragmatic.Persistence.Entity`)

Time-bounded relationship (e.g., user-role assignment valid from Jan 1 to Jun 30).

```csharp
public interface ITemporalRelation
{
    DateTimeOffset ValidFrom { get; set; }
    DateTimeOffset? ValidTo { get; set; }    // Null means currently active (no end date)
}
```

`[TemporalRelation]` on the entity is what makes the generator emit the `Active()` and `ActiveAt(date)` query extensions; the interface on its own generates nothing.

### `IEntityFactory<TEntity>` (`Pragmatic.Persistence.Entity`)

Factory for creating entity instances during mutations.

```csharp
public interface IEntityFactory<out TEntity> where TEntity : class
{
    TEntity Create();
}
```

Written to be implemented by the application, for entities whose construction needs something the generator cannot supply. The generated mutation invoker does not consult it: its `CreateEntity()` constructs the entity directly.

---

### `ICreatable<TSelf>` / `IEntityFactory<TEntity>` (`Pragmatic.Persistence.Entity`)

Two ways to construct entities correctly. `ICreatable<TSelf>` is implemented FOR you by the
generator: a compile-time `static Create()` factory (Guid v7 for a `Guid` id, audit stamps,
defaults) with zero DI; an `int` or `long` id is left unset, because that one belongs to the
database. `IEntityFactory<TEntity>` is the escape hatch when construction needs services, and the
application both implements and calls it: no generated code resolves it.

### `IOwnedEntity` / `IScopedEntity` (`Pragmatic.Persistence.Entity`)

Row-level data ownership. `IOwnedEntity.OwnerId` is auto-set from the current user on insert and
enforced by the ownership query filter ("you see what you created"). `IScopedEntity.AccessScopes`
is a list of scope tokens (`user:...`, `role:...`, `scope:dept-x`) matched against the user's
resolved scopes (`IUserScopeResolver`, section 5), so queries come back already filtered. Apply via
`[HasOwner]` / `[HasAccessScopes]` on the entity; the generator wires interfaces and filters.

## 3. Persistence -- Repositories

> ⚠️ `IReadRepository<TEntity>` and `IRepository<TEntity>` are documented here and **shipped in
> `Pragmatic.Persistence`**: their signatures name the query contracts and `PagedResult<T>`, which
> Abstractions cannot reference without a cycle through `Pragmatic.Result`. The namespace is
> unchanged (`Pragmatic.Persistence.Repository`), so nothing a consumer writes moves with them.

### `IReadRepository<TEntity>` (`Pragmatic.Persistence.Repository`)

Read-only repository. Uses `Specification<T>` from `Pragmatic.Specification` for composable query predicates.

```csharp
public interface IReadRepository<TEntity>
    where TEntity : class, IEntity
{
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<TEntity>> FindAsync(ISpecification<TEntity> spec, CancellationToken ct = default);
    Task<int> CountAsync(ISpecification<TEntity> spec, CancellationToken ct = default);
    Task<bool> ExistsAsync(ISpecification<TEntity> spec, CancellationToken ct = default);
    Task<TEntity?> FirstOrDefaultAsync(ISpecification<TEntity> spec, CancellationToken ct = default);
    IQueryable<TEntity> Query();
}
```

### `IRepository<TEntity>` (`Pragmatic.Persistence.Repository`)

Full CRUD repository, extending `IReadRepository`.

```csharp
public interface IRepository<TEntity> : IReadRepository<TEntity>
    where TEntity : class, IEntity
{
    void Add(TEntity entity);
    void AddRange(IEnumerable<TEntity> entities);
    void Remove(TEntity entity);
    void RemoveRange(IEnumerable<TEntity> entities);
    void Update(TEntity entity);
}
```

### `IUnitOfWork` (`Pragmatic.Persistence.Repository`)

Unit of Work pattern for transactional persistence.

```csharp
public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    void Add(object entity) { }                              // Default no-op; used by preset providers
    Task<ITransaction> BeginTransactionAsync(CancellationToken ct = default);
}
```

### `ITransaction` (`Pragmatic.Persistence.Repository`)

Database transaction handle.

```csharp
public interface ITransaction : IDisposable, IAsyncDisposable
{
    Guid TransactionId { get; }
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
```

---

## 4. Identity

> Want the detail? [how-it-works/identity](how-it-works/identity.md) covers how these fit together,
> which consumers were verified, and the two open questions this catalogue cannot show.

### `ICurrentUser` (`Pragmatic.Identity`)

The currently authenticated user in the request scope. Core contract consumed by persistence (auditing), actions (authorization), and endpoints (permission enforcement).

```csharp
public interface ICurrentUser
{
    string Id { get; }                                        // Empty for anonymous users
    string? DisplayName { get; }                              // Null for anonymous
    bool IsAuthenticated { get; }
    PrincipalKind Kind { get; }                               // Anonymous, User, Service, System
    string? TenantId { get; }                                 // Null when not applicable
    IReadOnlyDictionary<string, IReadOnlyList<string>> Claims { get; }  // Multi-valued claims
    string? ImpersonatedBy { get; }                           // Impersonator user ID, if any
    IUserAuthorization Authorization { get; }                 // Roles, permissions, groups, scopes
    IAuthenticationContext Authentication { get; }            // Scheme, issuer, MFA, expiry
}
```

### `IAuthenticationContext` (`Pragmatic.Identity`)

Authentication metadata. Accessed via `ICurrentUser.Authentication`.

```csharp
public interface IAuthenticationContext
{
    string? Scheme { get; }              // e.g., "Bearer", "Cookie"
    string? Protocol { get; }            // e.g., "oidc", "saml2", "apikey"
    string? Issuer { get; }              // e.g., "https://login.example.com"
    string? Subject { get; }             // Subject identifier from IdP
    bool IsMfaAuthenticated { get; }
    DateTimeOffset? AuthenticatedAt { get; }
    DateTimeOffset? ExpiresAt { get; }
    string? ExternalIdentityKey { get; } // "{issuer}|{subject}" format
}
```

### `IUserProfile` (`Pragmatic.Identity`)

User profile data, generated from `[PragmaticUser]` entity properties.

```csharp
public interface IUserProfile
{
    string? PreferredCulture { get; }    // e.g., "it-IT", "en-US"
    string? TimeZone { get; }            // IANA, e.g., "Europe/Rome"
    IReadOnlyDictionary<string, string?> Properties { get; }  // Additional profile properties
}
```

### `PrincipalKind` (`Pragmatic.Identity`)

```csharp
public enum PrincipalKind
{
    Anonymous,   // No authenticated identity
    User,        // Human user via identity provider
    Service,     // Service-to-service (API key, client credentials)
    System       // Background jobs, seed, migrations
}
```

### `AnonymousUser` (`Pragmatic.Identity`)

Singleton `ICurrentUser` for unauthenticated requests. `Id` is empty string, `IsAuthenticated` is false, `Authorization` returns `NullUserAuthorization.Instance`, `Authentication` returns `NullAuthenticationContext.Instance`.

```csharp
public static readonly AnonymousUser Instance;
```

### `NullAuthenticationContext` (`Pragmatic.Identity`)

Singleton `IAuthenticationContext` for anonymous/system contexts. All properties return null or false.

```csharp
public static readonly NullAuthenticationContext Instance;
```

### `CurrentUserExtensions` (`Pragmatic.Identity`)

```csharp
public static class CurrentUserExtensions
{
    static string? IdOrNull(this ICurrentUser user);
    static string DisplayNameOrId(this ICurrentUser user);
    static string? GetClaim(this ICurrentUser user, string type);
    static IReadOnlyList<string> GetClaims(this ICurrentUser user, string type);
    static string? GetClaimValue(this ICurrentUser user, string claimType);       // Alias for GetClaim
    static IReadOnlyList<string> GetClaimValues(this ICurrentUser user, string claimType);  // Alias for GetClaims
}
```

### `[PragmaticUser]` (`Pragmatic.Identity`)

Marks the application's user entity. The SG generates a profile adapter (`IUserProfile`) and a user resolver service.

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PragmaticUserAttribute : Attribute
{
    public string MatchClaim { get; set; } = "sub";   // Claim type for user matching
    public string? MatchProperty { get; set; }         // Entity property for matching (inferred from the entity when unset)
}
```

### `[ProfileProperty]` (`Pragmatic.Identity`)

Marks a property on a `[PragmaticUser]` entity for `IUserProfile` generation. Well-known names (`PreferredCulture`, `TimeZone`) map to dedicated accessors; others go into `Properties` dictionary.

```csharp
[AttributeUsage(AttributeTargets.Property)]
public sealed class ProfilePropertyAttribute : Attribute;
```

---

## 5. Authorization

### `IUserAuthorization` (`Pragmatic.Authorization`)

Authorization context for the current user. Accessed via `ICurrentUser.Authorization`.

```csharp
public interface IUserAuthorization
{
    IReadOnlyCollection<string> Roles { get; }
    IReadOnlySet<string> Permissions { get; }       // Expanded, cached per request
    IReadOnlyCollection<string> Groups { get; }
    IReadOnlyCollection<string> Scopes { get; }     // OAuth/OIDC scopes

    bool HasPermission(string permission);
    bool HasAnyPermission(IEnumerable<string> permissions);   // OR logic
    bool HasAllPermissions(IEnumerable<string> permissions);  // AND logic
    bool IsInRole(string role);
    bool IsInGroup(string group);
    bool HasScope(string scope);
}
```

### `[assembly: Permission]` (`Pragmatic.Authorization`)

Declares a custom permission of the assembly, one that is not an entity's CRUD permission. The generator
adds its `const` to the boundary's `{Boundary}Permissions` class and its entry to the permission registry.

```csharp
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class PermissionAttribute(string value, string description) : Attribute
{
    public string Value { get; }
    public string Description { get; }
    public string? Category { get; set; }           // e.g., "Privacy"
}

[assembly: Permission("leave.personal-data.erase", "Erase an employee's personal data", Category = "Privacy")]
// → LeavePermissions.PersonalData.Erase
```

### `IRole` (`Pragmatic.Authorization`)

Strongly-typed role with default permission assignments.

```csharp
public interface IRole
{
    static abstract string Name { get; }
    static abstract string? Description { get; }
    static abstract IReadOnlyList<string> DefaultPermissions { get; }  // Supports wildcards ("booking.*")
}
```

### `IGroup` (`Pragmatic.Authorization`)

Strongly-typed group with default role assignments.

```csharp
public interface IGroup
{
    static abstract string Name { get; }
    static abstract string? Description { get; }
    static abstract IReadOnlyList<string> DefaultRoles { get; }
}
```

### `IRoleDefinition` (`Pragmatic.Authorization`)

Permission template defined by a module. Not an application role. Used as a building block for composing real roles at the host level.

```csharp
public interface IRoleDefinition
{
    static abstract string Name { get; }
    static abstract string? Description { get; }
    static abstract IReadOnlyList<string> Permissions { get; }
}
```

**Example composition:**
```csharp
authz.MapRole("operations-manager", r => r
    .IncludeDefinition<BookingOperator>()
    .IncludeDefinition<CatalogReader>()
    .WithoutPermissions(BookingPermissions.Reservation.Delete));
```

### `IPermissionProvider` (`Pragmatic.Authorization`)

Resolves permissions from external sources. Multiple providers are composed and merged (union).

```csharp
public interface IPermissionProvider
{
    int Order { get; }   // Shipped: -1 composite, 0 claims, 100 role expansion, 200 group expansion
    ValueTask<IReadOnlySet<string>> ResolvePermissionsAsync(ICurrentUser user, CancellationToken ct = default);
}
```

### `IPermissionChecker` (`Pragmatic.Authorization`)

Async permission checker for scenarios requiring I/O (policy servers, database RBAC).

```csharp
public interface IPermissionChecker
{
    ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken = default);
    ValueTask<bool> HasAnyPermissionAsync(IEnumerable<string> permissions, CancellationToken cancellationToken = default);
    ValueTask<bool> HasAllPermissionsAsync(IEnumerable<string> permissions, CancellationToken cancellationToken = default);
}
```

**When to use:** `IUserAuthorization` is reached through `ICurrentUser`, and answers roles, groups and scopes straight off the claims. Permissions are not free: the shipped implementation fans out over the providers and passes through the cache stack, and the synchronous members block on that work. `IPermissionChecker` is the other seam, for a check that must go and ask something (a database, a directory, a remote policy service), and the signature is the warning that a call may cost.

### `IResourceAuthorizer<TResource>` (`Pragmatic.Authorization`)

Resource-level (ABAC) authorization. `TResource` is **not** contravariant: the filter resolves the authorizer by the resource's own type and the container applies no variance, so an authorizer declared for a base type would never be found for a derived one. Every type it guards needs its own registration. An unregistered type is allowed (the interface is opt-in) unless an authorizer is registered for one of its base types, in which case it is refused.

```csharp
public interface IResourceAuthorizer<TResource>
{
    ValueTask<bool> CanAccessAsync(
        ICurrentUser user,
        TResource resource,
        string action,
        CancellationToken ct = default);
}
```

### `NullUserAuthorization` (`Pragmatic.Authorization`)

Singleton. All checks return false, all collections empty.

```csharp
public static readonly NullUserAuthorization Instance;
```

### `FullAccessUserAuthorization` (`Pragmatic.Authorization`)

Singleton. All checks return true. Used for system-level contexts (background jobs, migrations).

```csharp
public static readonly FullAccessUserAuthorization Instance;
```

### `PermissionInfo` (`Pragmatic.Authorization`)

```csharp
public sealed record PermissionInfo(string Name, string? Description, string? Category);
```

### `RoleInfo` (`Pragmatic.Authorization`)

```csharp
public sealed record RoleInfo(string Name, string? Description, IReadOnlyList<string> DefaultPermissions);
```

### `[RequirePermission]` (`Pragmatic.Authorization`)

AND-logic permission enforcement. Applied to endpoints, actions, and mutations.

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequirePermissionAttribute(params string[] permissions) : Attribute
{
    public string[] Permissions { get; }
    public string? Description { get; set; }    // Declares the permission too (Mode 2)
    public string? Category { get; set; }       // Its registry category (Mode 2)
}
```

**Mode 1 (enforcement only):** `[RequirePermission("booking.reservation.create")]`
**Mode 2 (enforcement + declaration):** `[RequirePermission("billing.invoice.refund", Description = "Issue a refund")]` -- declares the permission as `[assembly: Permission]` would, with the full value as written.

### `[RequireAnyPermission]` (`Pragmatic.Authorization`)

OR-logic permission enforcement.

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequireAnyPermissionAttribute(params string[] permissions) : Attribute
{
    public string[] Permissions { get; }
}
```

### `[ExplicitPermission]` (`Pragmatic.Authorization`)

Overrides the auto-derived permission name. Pass a generated constant (an entity's CRUD, or an
`[assembly: Permission]`) so the value follows the declaration; one nothing generates is `PRAG0421`.

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ExplicitPermissionAttribute(string permission) : Attribute
{
    public string Permission { get; }
}

[ExplicitPermission(BillingPermissions.Invoice.Refund)]
```

---

### `IUserScopeResolver` (`Pragmatic.Authorization`)

Resolves the current user's data-access scopes (`user:...`, `role:...`, `scope:dept-x`) consumed by
the persistence filters on `IScopedEntity` data (section 2): queries return only rows whose
`AccessScopes` intersect the user's set. Implement it only when scopes don't come straight from
roles/claims:

```csharp
public class DepartmentScopeResolver : IUserScopeResolver
{
    public ValueTask<IReadOnlySet<string>> ResolveAccessScopesAsync(ICurrentUser u, CancellationToken ct)
        => new(new HashSet<string> { $"scope:dept-{u.GetClaim("dept")}" });
}
```

## 6. Events

### `IDomainEvent` (`Pragmatic.Events`)

Marker for domain events.

```csharp
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
```

Events should be immutable records capturing what happened.

### `IDomainEventDispatcher` (`Pragmatic.Events`)

Dispatches events to registered handlers.

```csharp
public interface IDomainEventDispatcher
{
    Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IDomainEvent;
    Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default);
}
```

### `IDomainEventHandler<TEvent>` (`Pragmatic.Events`)

Handles a specific event type. Multiple handlers per event type are supported, executed in ascending `Order`.

```csharp
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    int Order => 0;                           // Lower = earlier
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}
```

### `IHasDomainEvents` (`Pragmatic.Events`)

Entity that accumulates domain events for post-persistence dispatch.

```csharp
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
```

### `EntityPropertyChanged<TEntity>` (`Pragmatic.Events`)

Built-in event for property change propagation (cascade handlers).

```csharp
public sealed record EntityPropertyChanged<TEntity> : IDomainEvent where TEntity : class
{
    public required object EntityId { get; init; }
    public required string PropertyName { get; init; }
    public object? NewValue { get; init; }
    public object? OldValue { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
```

---

### `IIntegrationEvent` / `[PublicEvent]` / `[ObsoleteEvent]` (`Pragmatic.Events`)

The two-tier event model. A plain `IDomainEvent` is INTERNAL to its boundary; marking it
`[PublicEvent]` (or implementing `IIntegrationEvent`) declares it a cross-boundary contract,
surfaced in the generated AsyncAPI document and eligible for transport publishing.
`[ObsoleteEvent("remove-by")]` deprecates an event while keeping consumers compiling; the AsyncAPI
contract shows the deprecation.

### `IRaisesLifecycleEvents` (`Pragmatic.Events`)

Implemented by the generator on entities declaring `[Raises<TEvent>]` (section 18):
`LifecycleEventsInterceptor` calls `RaiseLifecycleEvents(transition)` during SaveChanges so declared
events fire on Created/Updated/Deleted with no hand-written code. That interceptor comes from
`UseDomainEvents` on the `DbContextOptionsBuilder`, and the wiring the generator emits for a
boundary's DbContext adds it on its own. It raises only: dispatch happens after the commit, in the
scope that asked for the write. You never implement the interface manually.

## 7. Temporal

### `IClock` (`Pragmatic.Temporal.Clock`)

Testable time abstraction wrapping `TimeProvider` for .NET 8+ interop.

```csharp
public interface IClock
{
    DateTimeOffset UtcNow { get; }
    DateTimeOffset Now { get; }
    DateOnly UtcToday { get; }
    DateOnly Today { get; }
    TimeOnly UtcTimeOfDay { get; }
    TimeOnly TimeOfDay { get; }
    TimeProvider GetTimeProvider();
}
```

---

## 8. Multi-Tenancy

### `ITenantContext` (`Pragmatic.MultiTenancy`)

Current tenant identity, scoped per request.

```csharp
public interface ITenantContext
{
    string? TenantId { get; }
    string? TenantName { get; }
    bool IsResolved { get; }
}
```

### `ITenantResolver` (`Pragmatic.MultiTenancy`)

Transport-agnostic tenant resolution.

```csharp
public interface ITenantResolver
{
    ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default);
}
```

Resolution strategies: HTTP header, subdomain, JWT claim, API key, route parameter.

### `ITenantEntity` (`Pragmatic.MultiTenancy`)

Marker for row-level tenant isolation.

```csharp
public interface ITenantEntity
{
    string TenantId { get; set; }    // Set automatically on creation, filtered on read
}
```

### `UnresolvedTenantContext` (`Pragmatic.MultiTenancy`)

Singleton fallback. All properties null, `IsResolved` is false.

```csharp
public static readonly UnresolvedTenantContext Instance;
```

---

### `IMutableTenantContext` (`Pragmatic.MultiTenancy`)

Lets NON-HTTP entry points (outbox delivery, jobs, message handlers) set the tenant on their DI
scope, the same scoped context the EF tenant filter reads, so background work stays
tenant-isolated:

```csharp
using var _ = mutableTenantContext.SetTenant(message.TenantId, tenantName: null);
// everything resolved in this scope now sees the right tenant
```

### `ITenantStore` / `TenantInfo` / `TenantState` (`Pragmatic.MultiTenancy`)

Persistent tenant metadata: CRUD plus lifecycle (`Active`, `Migrating`, `Suspended`,
`Deactivated`, `Provisioning`). A non-null `TenantInfo.ConnectionString` means DB-per-tenant (and
is masked in ToString/logs). `DeactivateAsync` is the supported "soft delete"; `DeleteAsync` is a
default-throwing member; implement it only if hard deletion is truly wanted.

## 9. Caching

### `ICacheStack` (`Pragmatic.Caching`)

Unified caching abstraction with stampede protection and tag-based invalidation. Default implementation: `HybridCacheStack` in `Pragmatic.Caching` (L1 memory + L2 distributed via `Microsoft.Extensions.Caching.Hybrid`).

```csharp
public interface ICacheStack
{
    ValueTask<T> GetOrSetAsync<T>(string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CacheEntryOptions? options = null, CancellationToken ct = default);

    ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default);

    ValueTask<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default);

    ValueTask SetAsync<T>(string key, T value,
        CacheEntryOptions? options = null, CancellationToken ct = default);

    ValueTask RemoveAsync(string key, CancellationToken ct = default);

    ValueTask InvalidateByTagAsync(string tag, CancellationToken ct = default);
    ValueTask InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default);
}
```

### `CacheEntryOptions` (`Pragmatic.Caching`)

Options for a cache entry including duration, tags, and eviction priority.

```csharp
public sealed class CacheEntryOptions
{
    public TimeSpan? Duration { get; init; }
    public TimeSpan? SlidingDuration { get; init; }
    public ImmutableArray<string> Tags { get; init; } = [];
    public CachePriority Priority { get; init; } = CachePriority.Normal;

    public static CacheEntryOptions Default { get; }               // 5 min duration
    public static CacheEntryOptions WithDuration(TimeSpan duration);
    public static CacheEntryOptions WithSliding(TimeSpan slidingDuration);
}
```

### `CachePriority` (`Pragmatic.Caching`)

Eviction priority under memory pressure.

```csharp
public enum CachePriority
{
    Low = 0,        // First to be evicted
    Normal = 1,     // Default
    High = 2,       // Less likely to be evicted
    NeverRemove = 3 // Never auto-evicted (use sparingly)
}
```

### `CacheCategories` (`Pragmatic.Caching`)

Predefined category marker types for routing cache operations to their own keyed stack. Each nested sealed class is used as a generic type parameter with `CachingBuilder.ForCategory<T>()` and `CacheStackProvider.ForCategory<T>()`; what a category carries is a key prefix and a default duration over the same underlying stack, not a backend of its own.

```csharp
public static class CacheCategories
{
    public sealed class Default;        // General-purpose business cache
    public sealed class OutputCache;    // ASP.NET Core HTTP response caching
    public sealed class RateLimiting;   // Cross-instance rate limit counters
    public sealed class Permissions;    // Authorization permission set caching
    public sealed class Configuration;  // Remote configuration value caching
    public sealed class Idempotency;    // Response replay for [Idempotent] endpoints
}
```

Custom categories are defined as additional sealed marker classes.

---

## 10. Configuration

### `IConfigurationStore` (`Pragmatic.Configuration`)

Backend-agnostic configuration store with tenant-scoped overrides and change watching.

```csharp
public interface IConfigurationStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task<string?> GetAsync(string key, string tenantId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, string tenantId, CancellationToken ct = default);
    Task SetAsync(string key, string value, string? tenantId = null, CancellationToken ct = default);
    Task DeleteAsync(string key, string? tenantId = null, CancellationToken ct = default);
    IAsyncEnumerable<ConfigurationChange> WatchAsync(string keyPattern, CancellationToken ct = default);
}
```

### `ISecretStore` (`Pragmatic.Configuration`)

Read-only store for secrets (vault, CI/CD, user-secrets).

```csharp
public interface ISecretStore
{
    Task<string?> GetSecretAsync(string key, CancellationToken ct = default);
    Task<string?> GetSecretAsync(string key, string tenantId, CancellationToken ct = default);
}
```

### `ConfigurationChange` (`Pragmatic.Configuration`)

```csharp
public sealed record ConfigurationChange(
    string Key, string? OldValue, string? NewValue, string? TenantId, DateTimeOffset Timestamp);
```

### `EnvironmentProfile` (`Pragmatic.Configuration`)

Wraps `IHostEnvironment` with Pragmatic conventions.

```csharp
public sealed class EnvironmentProfile
{
    public required string Name { get; init; }       // e.g., "Development", "Production"
    public string? Tag { get; init; }                // e.g., "eu-west", "canary"
    public IReadOnlyList<string> ResolutionChain { get; init; }  // e.g., ["base", "staging", "staging-eu-west"]

    public bool IsDevelopment { get; }
    public bool IsStaging { get; }
    public bool IsProduction { get; }
    public bool IsTesting { get; }
    public bool IsEnvironment(string environmentName);

    public static EnvironmentProfile From(string environmentName, string? tag = null);
}
```

---

### `ISecretStore` / `SecretEntry` (`Pragmatic.Configuration`)

Read-only secret access (secrets are set out-of-band: Key Vault, env, ops tooling).
`GetSecretWithMetadataAsync` returns a `SecretEntry` carrying rotation/expiry metadata
(`ExpiresAt`, `RotatedAt`, `IsExpired(now)`, `NotFound` sentinel) so consumers can react to stale
secrets instead of failing cryptically.

### `IConfigurationAuditStore` / `ConfigurationAuditEntry` (`Pragmatic.Configuration`)

Read-only access to the configuration audit log (who changed what, when, old/new value, tenant).
Populated by the database-backed configuration store; consumed by admin/ops surfaces.

## 11. Feature Flags

### `IFeatureFlag` (`Pragmatic.FeatureFlags`)

Strongly-typed feature flag marker.

```csharp
public interface IFeatureFlag
{
    static abstract string Name { get; }
    static abstract string? Description { get; }
}
```

### `IFeatureFlagStore` (`Pragmatic.FeatureFlags`)

Evaluation-aware flag store with context-based targeting.

```csharp
public interface IFeatureFlagStore
{
    Task<bool> IsEnabledAsync(string flagName, CancellationToken ct = default);
    Task<bool> IsEnabledAsync(string flagName, FeatureFlagContext context, CancellationToken ct = default);
    Task<FeatureFlagDefinition?> GetDefinitionAsync(string flagName, CancellationToken ct = default);
    Task<IReadOnlyList<FeatureFlagDefinition>> GetAllAsync(CancellationToken ct = default);
    IAsyncEnumerable<FeatureFlagChange> WatchAsync(CancellationToken ct = default);
}
```

### `IFeatureFlagContextProvider` (`Pragmatic.FeatureFlags`)

Builds evaluation context from ambient state.

```csharp
public interface IFeatureFlagContextProvider
{
    Task<FeatureFlagContext> GetContextAsync(CancellationToken ct = default);
}
```

### `FeatureFlagContext` (`Pragmatic.FeatureFlags`)

```csharp
public sealed record FeatureFlagContext
{
    public string? TenantId { get; init; }
    public string? UserId { get; init; }
    public string? Plan { get; init; }
    public string? Environment { get; init; }
    public IReadOnlyDictionary<string, string> Properties { get; init; }
    public static FeatureFlagContext Empty { get; }
}
```

### `FeatureFlagDefinition` (`Pragmatic.FeatureFlags`)

```csharp
public sealed record FeatureFlagDefinition
{
    public required string Name { get; init; }
    public bool Enabled { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<FeatureFlagRule> Rules { get; init; }
}
```

### `FeatureFlagRule` (`Pragmatic.FeatureFlags`)

Defined in `FeatureFlagDefinition.cs` alongside `FeatureFlagDefinition`.

```csharp
public sealed record FeatureFlagRule
{
    public required string Type { get; init; }       // "tenant", "user", "plan", "percentage", "property"
    public IReadOnlyList<string> Values { get; init; }
    public bool Enabled { get; init; } = true;
}
```

### `FeatureFlagChange` (`Pragmatic.FeatureFlags`)

```csharp
public sealed record FeatureFlagChange(
    string FlagName, bool WasEnabled, bool IsEnabled, DateTimeOffset Timestamp);
```

### `FeatureFlagStoreExtensions` (`Pragmatic.FeatureFlags`)

```csharp
public static class FeatureFlagStoreExtensions
{
    static Task<bool> IsEnabledAsync<TFlag>(this IFeatureFlagStore store, CancellationToken ct = default);
    static Task<bool> IsEnabledAsync<TFlag>(this IFeatureFlagStore store, FeatureFlagContext context, CancellationToken ct = default);
    static Task<FeatureFlagDefinition?> GetDefinitionAsync<TFlag>(this IFeatureFlagStore store, CancellationToken ct = default);
}
```

---

## 12. Internationalization

### `IGlobalizationContext` (`Pragmatic.Internationalization.Context`)

Current globalization context for formatting operations.

```csharp
public interface IGlobalizationContext
{
    CultureInfo Culture { get; }
    TimeZoneInfo? TimeZone => null;      // Null falls back to UTC
    string? CurrencyCode => null;        // e.g., "EUR", "USD"; null derives from culture
}
```

---

## 13. Pipeline

### `ICallContext` (`Pragmatic.Pipeline`)

Tracks whether the current execution is system-initiated (internal call). When `IsInternalCall` is true, authorization filters skip permission checks. Event handlers automatically run as internal calls.

```csharp
public interface ICallContext
{
    bool IsInternalCall { get; }
    IDisposable EnterInternalCall();     // Returns disposable that restores previous state; supports nesting
}
```

---

## 14. Composition

### `IPragmaticBuilder` (`Pragmatic.Composition`)

Fluent builder for configuring module strategies at startup.

```csharp
public interface IPragmaticBuilder
{
    IServiceCollection Services { get; }
    IConfiguration Configuration { get; }
    IHostEnvironment Environment { get; }
}
```

Each module contributes `Use*()` extension methods on this interface (e.g., `UseMultiTenancy`, `UseAuthentication`).

### `IPackageDefinition` (`Pragmatic.Composition`)

Defines a reusable package that can be imported into a module via `[UsePackage<T>]`.

```csharp
public interface IPackageDefinition
{
    static abstract string PackageName { get; }
    static abstract string? RoutePrefix { get; }
    static abstract string? Description { get; }
}
```

### `ServiceCollectionDecorateExtensions` (`Pragmatic.Composition.Extensions`)

Decorator support for `IServiceCollection`.

```csharp
public static class ServiceCollectionDecorateExtensions
{
    static IServiceCollection Decorate<TService, TDecorator>(this IServiceCollection services);
    static IServiceCollection Decorate(this IServiceCollection services, Type serviceType, Type decoratorType);
    static IServiceCollection Decorate<TService>(this IServiceCollection services, Func<TService, IServiceProvider, TService> decorator);
}
```

### `PragmaticDatabase` (`Pragmatic.Composition.Database`)

Abstract base class for database declarations.

```csharp
public abstract class PragmaticDatabase
{
    public virtual IEnumerable<string> GetRequiredConfigKeys();
}
```

---

### `IEndpointRouteBuilderConfigurator` (`Pragmatic.Composition`)

Hook for modules that need to map custom endpoints on the WebApplication without taking an ASP.NET
dependency in the contract (the `app` parameter is typed `object` and cast by the host).
Implementations are discovered and invoked by the host at startup.

### Assembly metadata: `[PragmaticMetadata]` / `IAssemblyMetadataProvider` / `AssemblyMetadataRegistry` (`Pragmatic.Composition.Metadata`)

The reflection-free channel modules use to describe themselves to hosts. It is **two** channels over
one attribute, and conflating them makes neither make sense:

- **Compile time.** The generator emits `[assembly: PragmaticMetadata(category, schemaVersion, json)]`,
  and the *host generator* reads those attributes straight off the referenced assemblies through
  Roslyn (`assembly.GetAttributes()`, no registry involved). This is what aggregates cross-assembly
  topology and why `[Include<Module>]` "just works" without scanning.
- **Run time.** The generator also registers a provider into `AssemblyMetadataRegistry` from a module
  initializer, for code that needs the same description while the application is running.

The compile-time half cannot use the registry: the host generator runs before any of that code
exists. You never touch either directly.

⚠️ The compile-time half reads **referenced** assemblies. A host that declares framework types itself
publishes the attribute in the same compilation that would have to read it, so those registrations
are handed over in-process instead; that seam is what `Pragmatic.Composition.HostWiring.Tests`
measures, and it went unnoticed long enough for eleven features to be silently inert for host-declared
types.

## 15. Composition Attributes

All in `Pragmatic.Composition.Attributes`. See the [README](../README.md#composition-attributes-pragmaticcompositionattributes) for a summary table. Full signatures:

### `[Module]`
```csharp
public sealed class ModuleAttribute : Attribute
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
}
```

### `[Service]` / `[Service<TInterface>]`
```csharp
public sealed class ServiceAttribute : Attribute
{
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Scoped;
    public Type? As { get; set; }
    public bool AsSelf { get; set; }
    public string? Key { get; set; }
}

public sealed class ServiceAttribute<TInterface> : Attribute where TInterface : class
{
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Scoped;
    public string? Key { get; set; }
}
```

### `[Decorator]`
```csharp
public sealed class DecoratorAttribute : Attribute
{
    public int Order { get; set; }
}
```

### `[Inject]`
```csharp
public sealed class InjectAttribute : Attribute
{
    public bool Required { get; set; }
    public string? Key { get; set; }
}
```

### `[ServiceFactory]` / `[Factory]`
```csharp
public sealed class ServiceFactoryAttribute : Attribute;

public sealed class FactoryAttribute : Attribute
{
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Scoped;
}
```

### `[Include<...>]` (3 overloads)
```csharp
public sealed class IncludeAttribute<TModule> : Attribute;
public sealed class IncludeAttribute<TModule, TDatabase> : Attribute;
public sealed class IncludeAttribute<TModule, TDatabase, TDbContext> : Attribute;
```

### `[IncludeModule<TModule>]`
```csharp
public sealed class IncludeModuleAttribute<TModule> : Attribute;
```

The one way a module declares a dependency on another. There is no non-generic form, no
`[DependsOn]` attribute and no `[Module(DependsOn = …)]` property. Before 1.0 a superseded spelling
is removed rather than deprecated (see `docs/CONVENTIONS.md`).

### `[StartupStep]`
```csharp
public sealed class StartupStepAttribute : Attribute;
```

### `[PragmaticDatabase]`
```csharp
public sealed class PragmaticDatabaseAttribute : Attribute
{
    public DatabaseProvider Provider { get; set; }
    public string ConfigKey { get; set; }
}
```

### `[RequiresConfig]`
```csharp
public sealed class RequiresConfigAttribute : Attribute
{
    public string SectionPath { get; }
    public string? Description { get; set; }
}
```

### `[UsePackage<TPackage>]`
```csharp
public sealed class UsePackageAttribute<TPackage> : Attribute where TPackage : class, IPackageDefinition
{
    public string? RoutePrefix { get; set; }
}
```

### `[RemoteBoundary<TModule>]`
```csharp
public sealed class RemoteBoundaryAttribute<TModule> : Attribute where TModule : class
{
    public string? BaseUrl { get; set; }
}
```

### `[PragmaticMetadata]`
```csharp
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class PragmaticMetadataAttribute : Attribute
{
    public MetadataCategory Category { get; }
    public string SchemaVersion { get; }
    public string JsonData { get; }
}
```

---

## 16. Composition Enums

### `ServiceLifetime` (`Pragmatic.Composition.Attributes`)
```csharp
public enum ServiceLifetime { Singleton = 0, Scoped = 1, Transient = 2 }
```

### `DatabaseProvider` (`Pragmatic.Composition.Enums`)
```csharp
public enum DatabaseProvider { SqlServer, PostgreSql, SQLite, MySql, InMemory }
```

### `MetadataCategory` (`Pragmatic.Composition.Metadata`)
```csharp
public enum MetadataCategory
{
    DI = 0, Mapping = 1, Actions = 2, Startup = 3, Validation = 4,
    Endpoints = 5, HealthChecks = 6, Identifiers = 7, Module = 8,
    Translations = 9, Persistence = 10, EventHandlers = 11,
    HostTopology = 12, Caching = 13, Configuration = 14
}
```

---

## 17. Telemetry

### `ActivityHelper` (`Pragmatic.Telemetry`)

Extension methods for `System.Diagnostics.Activity`.

```csharp
public static class ActivityHelper
{
    static Activity? RecordException(this Activity? activity, Exception ex);
    static Activity? SetSuccess(this Activity? activity);
    static Activity? SetFailure(this Activity? activity, string errorCode, string? description = null);
    static Activity? AddNamedEvent(this Activity? activity, string name, params KeyValuePair<string, object?>[] tags);
}
```

### `TelemetryOptions` (`Pragmatic.Telemetry`)

```csharp
public sealed class TelemetryOptions
{
    public bool Enabled { get; set; } = true;
    public bool Tracing { get; set; } = true;
    public bool Metrics { get; set; } = true;
    public bool Logging { get; set; } = true;
    public bool UseOtlpExporter { get; set; }       // Default false; console in Development
    public string? ServiceName { get; set; }         // Null = assembly name
    public double SamplingRatio { get; set; } = 0.1; // 10% in production, always 1.0 in dev
}
```

### Telemetry Convention Classes (`Pragmatic.Telemetry.Conventions`)

| Class | Tag Constants |
|---|---|
| `ErrorTags` | `Type`, `Message`, `Stacktrace` |
| `ActionTags` | `Name`, `Kind`, `Result`, `ErrorCode`, `MutationMode`, `EntityType`, `FilterCount` |
| `DbTags` | `System`, `Operation`, `CollectionName`, `RowsAffected`, `BulkBatchSize`, `FilterCount`, `FilterMode` |
| `CacheTags` | `Hit`, `Key`, `Operation`, `Tags` |
| `ResilienceTags` | `Policy`, `Attempt`, `Outcome`, `CircuitState` |
| `EventTags` | `Name`, `Handler`, `HandlerCount` |
| `MessagingTags` | `MessageType`, `HandlerType`, `Transport`, `Result`, `RetryAttempt`, `Deduplicated`, `SagaType`, `SagaState` |
| `JobTags` | `JobType`, `JobId`, `Result`, `RetryAttempt`, `RecurringId`, `CronExpression`, `DurationMs` |
| `I18NTags` | `UICulture`, `DataCulture`, `Currency`, `TimeZone`, `Source`, `RequestedCulture`, `ResolvedCulture`, `ValidationResult`, `ProviderName`, `ProviderPriority`, `LocalizationKey`, `LocalizationCulture`, `FallbackUsed`, `Missing`, `Count`, `PluralCategory`, `FilePath`, `StringsCount`, `PluralsCount` |

## 18. Authoring

Living-specification markers: requirements travel *with* the code and stay queryable.

### `Behavior` / `PendingBehaviorException` (`Pragmatic.Authoring`)

Marks unimplemented business logic. The body compiles and satisfies any return type; callers get a
dedicated exception (distinguishable from .NET's `NotImplementedException`), so "what is still
pending?" is an exact search, not a guess.

```csharp
public decimal CalculateRefund(Reservation r) => throw Behavior.Pending("UC-12 partial refund");
```

**What you get:** the source generator detects endpoints whose body is still pending and emits
`[assembly: PendingContract(...)]`; the contract-test generator then *skips* those endpoints, so a
freshly scaffolded app is green on what exists and silent (not red) on what is declared but pending.

### `[Raises<TEvent>]` (`Pragmatic.Authoring`)

Declares the domain event a member raises. On an **entity**, the generator wires the raise for you
at the chosen lifecycle transition (`Created` default, `Updated`, `Deleted`), filling the event's
constructor from matching entity members by name, with zero code. On a **method/mutation**, the generator
wires the raise as well; do not raise the event by hand too, or it goes out twice.

```csharp
[Raises<ReservationCreated>]                       // auto-raised when the entity is created
public partial class Reservation { }
```

**What you get:** the event graph is validated at compile time (declared-but-unhandled events →
PRAG0816, event cycles → analyzer) and feeds the generated AsyncAPI contract.

> Not to be confused with `[RaisesEvent<T>]` (Pragmatic.Persistence), which attaches events to
> state-machine **enum values** ("when the state becomes Paid, raise InvoicePaid").

### `[Rule]` / `[UseCase]` (`Pragmatic.Authoring`)

Annotate business rules and use-case ids directly on the classes/methods that implement them:

```csharp
[UseCase("UC-12", Title = "Partial refund")]
[Rule("A refund can never exceed the amount paid")]
public partial class RefundInvoiceAction { }
```

**What you get:** requirements that survive refactorings and answer "which code implements UC-12?"
with an exact search. By design these are annotation-only: no framework tool consumes them today;
they are meant for manual traceability and for external tooling.

## 19. Maintenance

### `IMaintenanceMode` (`Pragmatic.Maintenance`)

Runtime maintenance switch, reference-counted and thread-safe: dispose the handle to deactivate.

```csharp
public interface IMaintenanceMode
{
    bool IsActive { get; }
    string? Reason { get; }
    DateTimeOffset? ActivatedAt { get; }
    DateTimeOffset? EstimatedEnd { get; }
    IDisposable Activate(string reason, TimeSpan? estimatedDuration = null);
}
```

**When to use:** you rarely call it; `app.UseMaintenanceMode()` registers the service together with
its options and the migration progress stream, and a control-plane `EnterMaintenanceCommand` flips
it. What that registers is the switch, not the pipeline: the 503 middleware and the admin panel are
the job of `MaintenanceStep`, which nothing adds to a generated host. Implement
`IMaintenanceModeObserver` to be notified on activate/deactivate (e.g. control-plane broadcast).

### `IMigrationProgressStream` / `MigrationProgressEvent` (`Pragmatic.Maintenance`)

Producer/consumer channel for migration progress: producers `Report(...)`, consumers
`StreamAsync(ct)` (an `IAsyncEnumerable`); this is what feeds the maintenance panel's SSE stream.
`MigrationProgressEvent` validates in its constructor (percent 0–100 inclusive) and timestamps
itself; its setters are `init`, so build these events positionally: an object initializer or a
`with` assigns over the validated value.

## 20. Control Plane

Contracts for coordinating a fleet of hosts (identity, state, health, commands, events). Local
no-op implementations live in the Composition host; the Agent-backed implementation
(`AgentControlPlane`) ships in `Pragmatic.Agent.Client` and activates via `UseAgent()`. You consume
these when building operational tooling.

### `IControlPlane` (`Pragmatic.ControlPlane`)

```csharp
public interface IControlPlane
{
    bool IsConnected { get; }
    Task ReportStatusAsync(...);                       // heartbeat + state
    Task<IReadOnlyList<HostInfo>> GetAllHostsAsync(...);
    IAsyncEnumerable<ControlPlaneEvent> StreamEventsAsync(...);
    Task SendCommandAsync(string hostId, HostCommand command, ...);
    Task BroadcastEventAsync(ControlPlaneEvent evt, ...);
}
```

### `HostCommand` hierarchy (`Pragmatic.ControlPlane`)

Polymorphic commands (`DrainCommand`, `MigrateCommand`, `EnterMaintenanceCommand`,
`ExitMaintenanceCommand`) serialized with `$type`. `IHostCommandDispatcher` routes a serialized
command to its `IHostCommandHandler<TCommand>`; implement a handler to support a new command.
Dispatch is allow-listed by design: unknown command types are rejected, never activated by name.

### Host identity, state and health (`Pragmatic.ControlPlane`)

- `IHostIdentity` (who am I): `HostId`, `HostName`, `HostType` (Tenant/Admin/Worker/Gateway), `StartedAt`.
- `IHostStatus`: mutable runtime state (`HostState`: Starting → Ready → Migrating/Maintenance/Draining → Drained or Stopped) with `TransitionTo(...)`. `Drained` is out of the rotation with nothing in flight and still running; `ExitMaintenanceCommand` returns it to `Ready`.
- `IHostHealthContributor`: implement to plug a component into the composite health report
  (`Name`, `Category`, `Mode` Pull/Push, `CheckAsync` → Healthy/Degraded/Unhealthy);
  `IHostHealthAggregator` combines them. Messaging transports ship contributors out of the box.
- `MigrationStatus`: snapshot of migration progress per database, validated in the constructor
  alone: its `init` properties let an object initializer or a `with` assign an inconsistent pair.
- `ControlPlaneError`: the `IError` (502) returned by control-plane operations, with factories
  (`HostNotFound`, `NotConnected`, `CommandFailed`).
- `ControlPlaneEvent` records: `HostStateChangedEvent` and `ConfigChangedEvent`, the two the KV watch
  maps (`state/app:` and `config/` keys). Migration progress is reported through `MigrationStatus` on
  `IHostStatus`, not as an event.

## 21. Serialization (AOT-first JSON seam)

One shared, source-generated JSON pipeline for messaging payloads, outbox, sagas, jobs and host,
reflection-free by default, so native AOT publishes cleanly.

### `[assembly: PragmaticGenerateJsonContext]` (`Pragmatic.Serialization`)

**When to use:** add it once per module when you target native AOT (or want reflection-free JSON).

```csharp
[assembly: PragmaticGenerateJsonContext]
```

**What you get:** the source generator emits a `JsonSerializerContext` covering your boundary types
(message/job/event/saga payloads, mapped DTOs, transitively closed) and registers it. No hand-written
context, no IL2026/IL3050 warnings. The PRAG2800 analyzer (see section 26) reports an uncovered
payload on message, domain-event and job handlers (at `Info` severity, and only once the assembly
declares a context of its own) with a code-fix.

### `PragmaticJsonOptions` (`Pragmatic.Serialization`)

The seam itself: an ordered chain of `JsonSerializerContext`s plus an opt-out reflection fallback,
built once and cached. Configure it in the host with `UseJson(...)` / `UseJson<TContext>()`, or let
`AddPragmaticJsonContext` (called by generated code) contribute contexts idempotently.
`DisableReflectionFallback()` makes any uncovered type a hard error, which is recommended for AOT.

## 22. Specification

### `ISpecification<T>` (`Pragmatic.Specification`)

Composable predicate usable both against `IQueryable` (translated to SQL) and in memory:
`ToExpression()` for query translation, `IsSatisfiedBy(entity)` for direct checks. Consumed by
`IReadRepository.FindAsync(spec)`. The base class and combinators (And/Or/Not) live in
Pragmatic.Specification; see that module's docs.

## 23. Pagination

### `Page<T>` (`Pragmatic.Pagination`)

A plain page, never a failure: `Items`, `TotalCount`, `Number` (1-based), `PageSize` plus derived
`TotalPages`, `HasPreviousPage`, `HasNextPage`. Returned by `ToPagedDtoAsync()` in Mapping.EFCore.

It was `PagedResult<T>`, the name of the Persistence result of a paged query
(`Pragmatic.Persistence.Query.Results`, success or failure, what generated grid queries return), and
was renamed so the two stop sharing a name. The page number is `Number` because C# refuses
a member named after its type.

## 24. Http

### `MaxBodySizeMetadata` (`Pragmatic.Http`)

Endpoint metadata carrying the request-body size limit in bytes. Emitted by the generator from
`[MaxBodySize(...)]` on an endpoint; enforced by the host's request-limits startup step. You never
construct it yourself.

## 25. Top-level attributes

### `[FastEnum]` (`Pragmatic`)

**When to use:** on any enum you convert to string / parse / enumerate on hot paths.

```csharp
[FastEnum]
public enum ReservationStatus { Draft, Confirmed, Cancelled }
```

**What you get:** generated zero-allocation, reflection-free helpers (`ToStringFast()`,
`IsDefined`, `TryParse`, `GetValues`, `GetNames`) instead of the slow reflection-based `Enum` APIs.

### `[NotLogged]` (`Pragmatic`)

Marks a property or parameter as sensitive (passwords, tokens, PII): a value that must not reach a
log, telemetry, an audit trail or a diagnostic.

```csharp
public sealed record RegisterUser(string Email, [property: NotLogged] string Password);
```

> **Current status: the attribute marks, nothing enforces it.** The generator reads it on message
> types and emits an `IRedactionMap` per messaging assembly, but no component consults that map:
> message auditing moved onto the framework audit trail, whose entries carry no payload field, so
> nothing is serialized there to redact. Hand-written `_logger.Log*` calls, generated `ToString()`
> and OpenAPI flagging are unaffected and always were.
>
> Redaction that does happen is pattern-based, not marker-based: `PragmaticDataRedactor`
> (Pragmatic.Logging) on configured property-name patterns, `PersonalDataRedactor` on what the audit
> trail stores. For declared personal data with erasure and retention behind it, use
> `[PersonalData]` from `Pragmatic.Privacy.Abstractions`.

## 26. Analyzers (`Pragmatic.Abstractions.Analyzers`)

Shipped with the package to guard the contracts above at compile time:

| Analyzer | Catches |
|---|---|
| `CaptiveDependencyAnalyzer` | A Scoped/Transient service captured by a Singleton (classic DI bug: stale dependency) |
| `BuildServiceProviderAnalyzer` | `BuildServiceProvider()` calls that create a second container (memory leaks, split singletons) |
| `InjectRequiredAnalyzer` | `[Inject]` usage inconsistencies (e.g. required injection on nullable/unset members) |
| `JsonContextCoverageAnalyzer` (PRAG2800) | A message, domain-event or job payload that no `[JsonSerializable]` in the assembly covers, with an `Add [JsonSerializable]` code-fix |
