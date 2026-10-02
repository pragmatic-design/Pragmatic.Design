# Persistence, entities — what a row knows about itself

> **Scope**: `src/Pragmatic.Abstractions/Persistence/Entity/` — the entity contracts.
> `IEntity` · `IAuditable` · `ISoftDelete` · `IChangeTracking` ·
> `ICreatable<TSelf>` · `IEntityFactory<TEntity>` · `IOwnedEntity` · `IScopedEntity` ·
> `ITemporalRelation`
>
> **Not covered here**: the attributes that turn these interfaces on — `[Entity]`,
> `[HasOwner]`, `[HasAccessScopes]`, `[SoftDelete]`, `[TemporalRelation]` — live in the
> `Pragmatic.Persistence` package, not here (see below). Repositories and unit of work are
> [their own document](persistence-repository.md); `ICurrentUser`, referenced by ownership and
> soft-delete metadata, is [identity](identity.md).

For the member-by-member catalogue, see [interfaces](../interfaces.md#2-persistence----entities).
This document is about how the pieces fit together.

## One namespace, two packages

`Pragmatic.Persistence.Entity` is split across two assemblies, and this is the first thing to know
because nothing in the folder layout shows it.

| What | Where |
|---|---|
| The interfaces on this page | `Pragmatic.Abstractions` |
| The attributes that activate them, and their support types (43 files) | `Pragmatic.Persistence` |

`IOwnedEntity` and `HasOwnerAttribute` read as neighbours — same namespace, adjacent names — and
ship in different NuGet packages. The split follows the rule that `Pragmatic.Abstractions` depends
on nothing: a contract can live there, an attribute that documents generator behaviour cannot. Nine
of those 43 files are not attributes, and one of them bends the rule: `IAuditedEntity` is a contract,
in the same namespace, that the generator adds to the base list next to `IAuditable` and
`ISoftDelete` — and it lives on the `Pragmatic.Persistence` side.

The practical consequence: referencing only `Pragmatic.Abstractions` gives you the interfaces to
write against, but not the attributes that make the generator implement them.

## Almost nobody writes these interfaces by hand

That is the shape decision behind the whole folder. These are not base classes to inherit and fill
in — they are the vocabulary the source generator and the runtime share. You annotate an entity, the
generator adds the interface *and* its implementation to your `partial class`, and the runtime
recognises the entity by the interface it now carries. Two are the exception: `IEntity` and
`ITemporalRelation` you write yourself in the entity's declaration, and the generator supplies the
implementation only.

| Interface | Emitted by | What appears on the entity |
|---|---|---|
| `IAuditable`, `ISoftDelete` | `EntityTraitsTemplate` | the audit columns, the soft-delete columns |
| `IEntity` — *you declare it* | `EntityTraitsTemplate` | `PersistenceId`, `Id` |
| `IChangeTracking` | `EntitySettersTemplate` | the modified-property set and `ResetModifiedProperties()` |
| `ICreatable<TSelf>` | `EntityCreateTemplate` | the static `Create()`, and the explicit interface implementation when the entity is `[Auditable]` |
| `IOwnedEntity` | `OwnershipPropertyTemplate` | `OwnerId` with a private setter, plus `SetOwnerId(...)` |
| `IScopedEntity` | `ScopedPropertyTemplate` | the access-scope collection |
| `ITemporalRelation` — *you declare it* | `TemporalQueryExtensionsTemplate`, `TemporalFilterTemplate`, `TimelineQueryTemplate`, `FilterMapRegistryTemplate` | `Active()` / `ActiveAt(date)` and the nested `TemporalFilter` |

`IEntityFactory<TEntity>` stands apart in this folder, and is worth reading its contract before
relying on it: it is written to be implemented by the application, for entities whose construction
needs something the generator cannot supply. The generated mutation invoker does not consult it —
`CreateEntity()` constructs the entity directly — so implementing it changes nothing on that path
today.

All of them sit under `Pragmatic.SourceGenerator/Features/Persistence/`.

## The contracts that carry a decision

### `ICreatable<TSelf>` — creation without a virtual call

`Create()` is a `static abstract` member, so a generic caller constrained on
`where T : ICreatable<T>` can write `T.Create()` and get compile-time dispatch: no factory
resolution, no virtual call. The generated `Create()` seeds a `Guid` persistence id with a
time-ordered Guid v7 (`Guid.CreateVersion7()`), plus audit timestamps and property defaults. An
`int` or `long` id is left unset on purpose — that one belongs to the database.

There is one condition worth knowing: the generator adds `ICreatable<T>` to the base list **only
when `Create()` has no required parameters**. An entity with `required` properties still gets a
`Create(...)` with those parameters, but not the interface — a parameterless contract cannot
describe a constructor that demands arguments.

### `IOwnedEntity` — ownership is assigned, never passed in

`OwnerId` has a private setter for a reason: it is not an input. The generated `CreateEntity()`
inside the mutation invoker reads `ICurrentUser.Id`, throws `InvalidOperationException` when there
is no current user, and only then calls `SetOwnerId`. Ownership cannot be spoofed through a DTO
because there is no path from a DTO to that property.

If you declare `OwnerId` yourself, the generator steps aside and emits an informational diagnostic
rather than fighting you for the member.

### `ISoftDelete` — deletion metadata written at two levels

The generated repository sets `DeletedBy` from `ICurrentUser?.IdOrNull()`, and the generated
mutation invoker does the same on its lifecycle path. `IdOrNull()` rather than `Id` because the
column is nullable and an empty string is not the same value as `NULL` to a query.

Downstream, the two halves are read by different things. The EF Core interceptors and the mutation
compensation path pattern-match on `ISoftDelete`; the generated query filters and the soft-delete
branch of the generated `Remove()` are gated on `[SoftDelete]` alone — they are typed classes,
emitted for an entity that never has to name the interface. The attribute is the signal that
carries: implement `ISoftDelete` by hand without it and you get an entity the interceptors
recognise and no filter excludes — deleted rows keep coming back from `GetByIdAsync`, `FindAsync`
and `Query()`, and the generated `Remove()` deletes the row for good.

### `ITemporalRelation` — the interface alone generates nothing

Like `ISoftDelete`, this contract does nothing on its own. The generator turns on
temporal output by reading `[TemporalRelation]` on the type; without the attribute, no `Active()`,
no `ActiveAt(date)`, no `FilterMap` registration. The interface tells the runtime what the rows
mean; the attribute tells the generator to emit the query surface.

It is the contract behind the assignment tables in `Pragmatic.Identity.Persistence` (`UserRole`,
`UserGroup`, `GroupRole`, `RolePermission`) and in `Pragmatic.Authorization.Management` — every
place where a link between two entities is true only for a period.

### `IChangeTracking` — what the setters record

Generated `Set{Property}` methods add the property name to the modified set as they write. The
mutation invoker calls `ResetModifiedProperties()` on the entity right after loading it, so the set
holds exactly what the current mutation touched, and hands that set to the validator on update
paths. In an `[Inheritance]` hierarchy the derived type's setters come from
`DerivedEntitySettersTemplate`, which writes the value and nothing else: properties declared on the
derived type never enter the set, and validation driven by it skips them. `ModifiedProperties`,
`CollectionsModified` and `IsNew` are all `[JsonIgnore]` — they are runtime state, not part of the
entity's serialized shape.

## External references

Named here, described where they live:

- **`Pragmatic.Persistence`** — the attribute half of the namespace, plus `Scopes/`
  (`ScopeMaterializer`, `ComputedScopeFilter`) which consumes `IScopedEntity`. `ISoftDelete` shows up
  in the package twice, both times in a comment — `IQueryFilter` names it inside an `<example>` —
  and nowhere in code that runs.
- **`Pragmatic.Persistence.EFCore`** — `AuditingInterceptor` fills `CreatedBy`/`UpdatedBy` from an
  *optional* `ICurrentUser`; with none supplied the columns are left untouched rather than filled
  with a placeholder. `SoftDeleteDetection` handles the delete path.
- **`Pragmatic.Actions`** — `MutationInvoker` owns the create/update/delete lifecycle: it resets
  change tracking, assigns ownership, and drives soft-delete compensation.
- **`Pragmatic.Events.EFCore`** — `LifecycleEventsInterceptor` reads `ISoftDelete` to tell a delete
  from an update.
- **`Pragmatic.Comments` / `Notes` / `Tags` / `Attachments`** — the trait base types build on
  `IEntity`; `NoteBase` and `AttachmentBase` also carry `ISoftDelete`.
- **`Pragmatic.Patch`** — `[GeneratePatch]` and `PatchTransform` reference `IAuditable`,
  `ISoftDelete` and `IOwnedEntity` when shaping the generated patch type.
