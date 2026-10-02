---
title: "Permission Resolution"
description: "This document explains how `Pragmatic.Authorization` resolves permissions at runtime: the provider chain, caching layers, and wildcard matching internals."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Authorization/docs/permission-resolution.md
sidebar:
  order: 3
---
This document explains how `Pragmatic.Authorization` resolves permissions at runtime: the provider chain, caching layers, and wildcard matching internals.

## Overview

Permission resolution is a three-phase process:

1. **Provider chain** -- Multiple `IPermissionProvider` implementations run in order, each contributing permissions.
2. **Caching** -- Results are cached per request (always) and optionally cross-request via `ICacheStack` (using `CacheCategories.Permissions`).
3. **Matching** -- `WildcardMatcher` checks if any resolved permission matches the required one.

## The Provider Chain

`IPermissionProvider` is the extension point. Each provider has an `Order` value that determines execution sequence. Results from all providers are merged with union semantics (additive only, no provider can remove permissions added by another).

```csharp
public interface IPermissionProvider
{
    int Order { get; }
    ValueTask<IReadOnlySet<string>> ResolvePermissionsAsync(
        ICurrentUser user, CancellationToken ct = default);
}
```

### Built-in Providers

| Provider | Order | Source | Registered when |
|----------|-------|--------|-----------------|
| `ClaimsPermissionProvider` | 0 | `ICurrentUser.Claims["permission"]` | Always |
| `RoleExpansionProvider` | 100 | `IRolePermissionStore` | Roles are mapped or custom store registered |
| `GroupExpansionProvider` | 200 | `IGroupRoleStore` + `IRolePermissionStore` | Groups are mapped or custom store registered |

### ClaimsPermissionProvider

The base provider. Reads the `"permission"` claim from `ICurrentUser.Claims` and returns each value as a permission string.

```
User claims: { "permission": ["booking.reservation.read", "catalog.amenity.read"] }
Result: HashSet { "booking.reservation.read", "catalog.amenity.read" }
```

### RoleExpansionProvider

For each `"role"` claim on the user, queries `IRolePermissionStore.GetPermissionsForRoleAsync(roleName)` and merges results.

```
User claims: { "role": ["booking-manager"] }

IRolePermissionStore lookup:
  "booking-manager" -> { "booking.reservation.*", "booking.guest.*", "catalog.property.read" }

Result: HashSet { "booking.reservation.*", "booking.guest.*", "catalog.property.read" }
```

### GroupExpansionProvider

For each `"group"` claim on the user, queries `IGroupRoleStore.GetRolesForGroupAsync(groupName)` to get roles, then queries `IRolePermissionStore.GetPermissionsForRoleAsync(roleName)` for each role.

```
User claims: { "group": ["customer-care"] }

IGroupRoleStore lookup:
  "customer-care" -> { "booking-manager", "catalog-viewer" }

IRolePermissionStore lookup:
  "booking-manager" -> { "booking.reservation.*", "booking.guest.*", "catalog.property.read" }
  "catalog-viewer" -> { "catalog.amenity.read", "catalog.property.read" }

Result: HashSet { "booking.reservation.*", "booking.guest.*", "catalog.property.read", "catalog.amenity.read" }
```

### Custom Providers

Register via `AuthorizationBuilder`:

```csharp
authz.AddPermissionProvider<ExternalPermissionProvider>();
```

Or via `CompositePermissionProvider`, which aggregates multiple `IPermissionProvider` instances excluding itself (to avoid recursion). `CompositePermissionProvider` has Order -1, running before all others.

### Resolution Flow

```
Request arrives
  |
  v
CachedPermissionResolver.ResolvePermissions()
  |
  +-- Cached? --> Return cached set
  |
  +-- ICacheStack enabled? --> Check ICacheStack (Permissions category)
  |   |
  |   +-- Cache hit? --> Return cached set
  |   |
  |   +-- Cache miss? --> Run provider chain, cache result
  |
  +-- No cross-request cache --> Run provider chain
  |
  v
For each IPermissionProvider (ordered by Order):
  Call ResolvePermissionsAsync(user)
  Merge into HashSet<string> (union)
  |
  v
Store result as _resolvedPermissions (request-scoped)
  |
  v
Return IReadOnlySet<string>
```

## CachedPermissionResolver

`CachedPermissionResolver` implements `IUserAuthorization` and is the runtime bridge between the provider chain and permission checks. It is registered as **Scoped** (one per request).

### Request-Scoped Caching

Permissions are resolved lazily on first access to `Permissions`, `HasPermission`, `HasAnyPermission`, or `HasAllPermissions`. The resolved set is stored in `_resolvedPermissions` and reused for all subsequent checks within the same request.

### Sync vs Async Checks

`CachedPermissionResolver` also implements the async members of `IUserAuthorization` —
`GetPermissionsAsync`, `HasPermissionAsync`, `HasAnyPermissionAsync`, `HasAllPermissionsAsync`. On a
cross-request cache miss these `await` the cache/provider chain instead of blocking a thread-pool thread,
so prefer them on request hot paths. The synchronous `HasPermission` overloads remain available for the
common warm-cache case and for the ASP.NET authorization bridge.

### Cross-Request Caching

When `UsePermissionCache` is configured and `ICacheStack` is available in DI, the resolved permissions are cached cross-request via the `CacheCategories.Permissions` category.

Cache key format (explicit scope markers avoid cross-tenant collisions):
- Global / single-tenant: `{prefix}:g:u:{userId}`
- Multi-tenant: `{prefix}:t:{tenantId}:u:{userId}`

Cache tags — the entry is tagged by everything that contributed to it:
- `user:{userId}`, `tenant:{tenantId}` (multi-tenant only)
- `role:{roleName}` for each role claim, `group:{groupName}` for each group claim

### Cache Invalidation

`UsePermissionCache` registers `IPermissionCacheInvalidator`. Inject it and call the method matching the
change instead of assembling tag strings yourself:

```csharp
public interface IPermissionCacheInvalidator
{
    ValueTask InvalidateUserAsync(string userId, CancellationToken ct = default);
    ValueTask InvalidateTenantAsync(string tenantId, CancellationToken ct = default);
    ValueTask InvalidateRoleAsync(string roleName, CancellationToken ct = default);   // reaches every holder of the role
    ValueTask InvalidateGroupAsync(string groupName, CancellationToken ct = default); // reaches every member of the group
}
```

```csharp
// After changing a user's roles or disabling the account:
await invalidator.InvalidateUserAsync(userId, ct);
```

Because entries are tagged by their contributing roles and groups, `InvalidateRoleAsync` /
`InvalidateGroupAsync` reach all affected users without enumerating them.

### Consistency Strategy

`PermissionCacheOptions.Strategy` (a `PermissionCacheStrategy`) selects how the cache stays consistent:

| Strategy | Behavior |
|----------|----------|
| `TimeToLive` (default) | The entry expires after `Expiration` and is re-resolved. A short expiration gives periodic re-validation; a long one favors throughput. Calling the invalidator is optional. |
| `ManualInvalidation` | The entry is kept until `IPermissionCacheInvalidator` evicts it — call it on every authorization change for immediate propagation. `Expiration` still applies as a safety-net upper bound. |

```csharp
authz.UsePermissionCache(o =>
{
    o.Expiration = TimeSpan.FromMinutes(30);
    o.Strategy = PermissionCacheStrategy.ManualInvalidation;
});
```

### Permission Matching

`HasPermission(string required)` checks in two steps:

1. **Fast path**: Exact match -- `resolved.Contains(required)`. This covers the most common case.
2. **Slow path**: Wildcard match -- for each resolved permission that contains `*`, checks `WildcardMatcher.Matches(granted, required)`.

```csharp
// Example: user has ["booking.*"]
// HasPermission("booking.reservation.read")
//   Step 1: "booking.reservation.read" not in set (set contains "booking.*") -> false
//   Step 2: "booking.*" contains '*' -> WildcardMatcher.Matches("booking.*", "booking.reservation.read") -> true
```

### Claims-Based Properties

`CachedPermissionResolver` also exposes claims-based properties:

| Property | Claim type | Fallback |
|----------|------------|----------|
| `Roles` | `role` | Empty array |
| `Groups` | `group` | Empty array |
| `Scopes` | `scope` | Empty array |

These are read directly from `ICurrentUser.Claims` without provider chain resolution.

## WildcardMatcher

`WildcardMatcher` is an internal static class that handles wildcard pattern matching for permissions. All matching is case-insensitive.

### Matching Algorithm

`Matches(string pattern, string permission)` has three paths:

1. **Exact match**: `string.Equals(pattern, permission, OrdinalIgnoreCase)`
2. **Global wildcard**: `pattern == "*"` matches everything
3. **Suffix wildcard**: Pattern like `"booking.*"` (ends with `.*`, no `*.` inside) -- checks if permission starts with the prefix (`"booking."`)
4. **Segment wildcard**: Pattern like `"booking.*.read"` or `"*.reservation.read"` -- splits both strings on `.` and compares segment by segment, with `*` matching exactly one segment

### Segment Matching Rules

When patterns and permissions have the same number of segments, each `*` matches any single segment:

```
Pattern:    booking.*.read
Permission: booking.reservation.read
Segments:   [booking, *, read] vs [booking, reservation, read]
Match:      booking=booking, *=reservation, read=read -> TRUE
```

When the pattern has a trailing `*` and fewer segments, it matches all remaining:

```
Pattern:    booking.*
Permission: booking.reservation.read
Trailing * detected -> check prefix: booking=booking -> TRUE
```

When segment counts differ and there is no trailing `*`, it does not match:

```
Pattern:    booking.*.read
Permission: booking.reservation
Segments:   3 vs 2, no trailing * -> FALSE
```

### ExpandWildcards

`ExpandWildcards(patterns, allKnownPermissions)` expands wildcard patterns against a known permission set. This is used for admin tooling (catalog) and `WithoutPermissions` validation.

```csharp
var patterns = new[] { "booking.*", "catalog.amenity.read" };
var allKnown = new HashSet<string>
{
    "booking.reservation.read",
    "booking.reservation.create",
    "booking.guest.read",
    "catalog.amenity.read",
    "catalog.property.read"
};

var expanded = WildcardMatcher.ExpandWildcards(patterns, allKnown);
// Result: { "booking.reservation.read", "booking.reservation.create",
//           "booking.guest.read", "catalog.amenity.read" }
```

## Observability

`CachedPermissionResolver` emits metrics via `AuthorizationDiagnostics`:

| Event | Counter |
|-------|---------|
| Permission check called | `permission_checks` +1 |
| Permission check denied | `permission_denied` +1 |
| Resolved set reused within request | `cache_hits` +1 |
| Resolved set computed (provider chain ran) | `cache_misses` +1 |

## Performance Characteristics

| Operation | Complexity |
|-----------|-----------|
| First permission check (cold) | O(P * R) where P = providers, R = roles/groups per user |
| Subsequent checks (warm) | O(1) exact match, O(N) wildcard scan where N = resolved permissions |
| Cross-request cache hit | O(1) ICacheStack lookup |
| Wildcard suffix match | O(1) string prefix check |
| Wildcard segment match | O(S) where S = number of segments |

In practice, the resolved permission set is small (tens to low hundreds), making the wildcard scan negligible.

## Configuration Reference

### AuthorizationOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EnablePermissionCaching` | `bool` | `true` | Enable request-scoped caching |
| `PermissionClaimType` | `string` | `"permission"` | Claim type for direct permissions |
| `CacheOptions` | `PermissionCacheOptions?` | `null` | Cross-request cache settings (null = disabled) |

### PermissionCacheOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Expiration` | `TimeSpan` | 5 minutes | Cache entry TTL (safety-net upper bound under `ManualInvalidation`) |
| `KeyPrefix` | `string` | `"permissions"` | Prefix for cache keys |
| `Strategy` | `PermissionCacheStrategy` | `TimeToLive` | `TimeToLive` or `ManualInvalidation` (see [Cache Invalidation](#cache-invalidation)) |
