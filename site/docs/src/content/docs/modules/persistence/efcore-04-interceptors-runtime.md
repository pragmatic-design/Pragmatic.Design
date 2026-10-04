---
title: "Interceptors & Runtime"
description: "Several fields on your entities need to be set automatically when saving to the database:"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Persistence/docs/efcore/04-interceptors-runtime.md
sidebar:
  order: 25
---
## The Problem

Several fields on your entities need to be set automatically when saving to the database:
- **ID generation**: New entities need a Guid7 (UUID v7) assigned before insert
- **Audit timestamps**: `CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy` must be stamped
- **Tenant ID**: Multi-tenant entities need `TenantId` set from the current request context

Doing this manually in every `Create()` call or service method is error-prone: forget one, and you have bad data.

## The Solution: Interceptors

EF Core interceptors run automatically before `SaveChanges`. They inspect every entity being saved and populate fields as needed. The source generator registers them for you.

## Where the ID comes from

Not from an interceptor. The generated entity assigns it **in its constructor**:

```csharp
// ═══ In {Entity}.Traits.g.cs, for an [Entity] ═══
public Guid PersistenceId { get; set; } = Guid.CreateVersion7();
```

The value is therefore final before EF Core sees the entity, which is what lets `AuditLogInterceptor`
record a real `EntityId` at `SavingChanges` time instead of a temporary one.

For `int` / `long` keys nothing is assigned: the database generates the value, and an `Added` entity
carries EF's temporary key until the insert completes. For `string` keys the caller provides it.

> **Why UUID v7.** A v4 GUID is random, so every insert lands at a random position in a clustered
> index and splits pages. A v7 embeds a millisecond timestamp in its first 48 bits, so new values sort
> after old ones and inserts append: the behaviour of an auto-increment key, without giving up global
> uniqueness.

⚠️ **SQL Server orders GUIDs differently**: it compares the last six bytes first, so a v7 does not
sort in insert order there. `Guid7.NewForSqlServer()` produces a byte-shuffled value that does, and
**the generated entity does not use it**: the constructor emits `Guid.CreateVersion7()` whatever the
provider is. Applying it would need the entity to know which database it will land in, and with more
than one database in a host that is not a static fact. There is no interceptor in this path: the key
is assigned by the constructor, before anything reaches the change tracker.

## AuditingInterceptor

### What it does

Populates audit fields on entities that implement `IAuditable`:

| Entity State | CreatedAt | CreatedBy | UpdatedAt | UpdatedBy |
|-------------|-----------|-----------|-----------|-----------|
| `Added` | Set to `now` | Set from `ICurrentUser` | Set to `now` | Set from `ICurrentUser` |
| `Modified` | Preserved | Preserved | Updated to `now` | Updated from `ICurrentUser` |

### Dependencies

```csharp
new AuditingInterceptor(TimeProvider.System, currentUser)
```

- **`TimeProvider`**: Abstraction over `DateTimeOffset.UtcNow`. Use `TimeProvider.System` in production. In tests, inject a fake `TimeProvider` for deterministic timestamps.
- **`ICurrentUser`**: Optional. If null, the `*By` fields remain null. When registered,
  `CreatedBy`/`UpdatedBy` is set from `ICurrentUser.Id`; the property is `Id`, and it is an **empty
  string** for an anonymous user, not null.

---

## TenantInterceptor

### What it does

For multi-tenant applications: automatically sets `TenantId` on entities that implement `ITenantEntity` when they are first saved.

| Condition | Action |
|-----------|--------|
| Entity is `Added` + implements `ITenantEntity` + `TenantId` is empty | Set from `ITenantContext.TenantId` |
| `TenantId` already has a value | Skip (don't overwrite) |
| Entity is `Modified` or `Deleted` | Skip (tenant is immutable after creation) |

This prevents a common bug: forgetting to set `TenantId` when creating an entity, which would make it invisible to tenant-scoped queries.

---

## Value Converters

Value converters transform property values between your C# types and the database.

### ShortGuidConverter

Converts a `Guid` to a 22-character URL-safe Base64 string. Useful for public-facing IDs in URLs.

```csharp
// In entity configuration
builder.Property(e => e.ExternalId).HasConversion<ShortGuidConverter>();

// C# value:  3F2504E0-4F89-11D3-9A0C-0305E82C3301
// DB value:  "4AUlP4lP00GaDCMF6CwzAQ"  (22 characters)
```

**When to use**: Public API responses, URL slugs, anywhere you want a shorter, URL-safe representation of a GUID.

### OpaqueIdConverter

Converts a `long` to an obfuscated string. Prevents **enumeration attacks** where an attacker guesses sequential IDs (`/users/1`, `/users/2`, `/users/3`...).

```csharp
builder.Property(e => e.PublicId).HasConversion(new OpaqueIdConverter("my-secret-salt"));

// C# value:  42
// DB value:  "kN7xPm"
```

**When to use**: Any integer ID exposed to end users where you don't want them to infer counts or guess other IDs.

| Converter | C# Type | DB Type | Nullable Variant |
|-----------|---------|---------|-----------------|
| `ShortGuidConverter` | `Guid` | `string(22)` | `NullableShortGuidConverter` |
| `OpaqueIdConverter` | `long` | `string` | `NullableOpaqueIdConverter` |

---

## Interceptor Registration

The generated `Add{Boundary}DbContext(...)` registers them for you, from what the boundary's entities
declare:

| What the boundary contains | Interceptor registered |
|---|---|
| Any `[Auditable]` | `AuditingInterceptor` |
| Any `ITenantEntity` | `TenantInterceptor` |
| Any `[SoftDelete]` | `SoftDeleteInterceptor`, which turns a delete into a flag at save time, on every path |
| Any `[HasOwner]` | `OwnershipInterceptor` |
| Any `[Audited]` | `AuditLogInterceptor`: the append-only `__AuditLog` row, in the same transaction |
| Any `[RollUp<T>]` | `RollUpInterceptor`, which keeps the parent's stored aggregate current |

There is no ID interceptor: the entity assigns its own `PersistenceId` (see *Where the ID comes from*).

You do not register these manually unless you want to change their behaviour.
