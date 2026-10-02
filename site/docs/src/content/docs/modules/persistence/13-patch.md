---
title: "Patch (Partial Updates)"
description: "> Track which properties were explicitly set and apply only those — for true HTTP PATCH semantics."
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Persistence/docs/13-patch.md
sidebar:
  order: 14
---
> Track which properties were explicitly set and apply only those — for true HTTP PATCH semantics.

## The Problem

`Mutation<TEntity>` + `[Mutation]` uses nullable properties to represent partial updates: if a property is `null`, it's not changed. But this approach has a limitation — you can't distinguish between "the client didn't send this field" and "the client sent `null` to clear this field".

Consider updating a user profile:

```json
// Client wants to clear the middle name
{ "middleName": null }

// Client didn't send middleName at all (should keep existing value)
{ "firstName": "Jane" }
```

With nullable properties alone, both cases look identical — `MiddleName` is `null`.

## The Solution: `[Patch<TEntity>]`

```csharp
[Patch<UserProfile>]
public partial class UpdateUserProfilePatch
{
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
}
```

### What the Source Generator Produces

```csharp
public partial class UpdateUserProfilePatch
{
    private HashSet<string> _setProperties = new();

    public IReadOnlySet<string> SetProperties => _setProperties;

    public void MarkSet(string propertyName) => _setProperties.Add(propertyName);

    public void ApplyPatch(UserProfile target)
    {
        if (_setProperties.Contains(nameof(FirstName)))
            target.SetFirstName(FirstName);
        if (_setProperties.Contains(nameof(MiddleName)))
            target.SetMiddleName(MiddleName);  // Can set to null!
        if (_setProperties.Contains(nameof(LastName)))
            target.SetLastName(LastName);
    }
}
```

### Key Difference from `Mutation<TEntity>` + `[Mutation]`

| Feature | `Mutation<TEntity>` + `[Mutation]` | `[Patch<T>]` |
|---------|-----------------|-------------|
| Tracks which properties are set | No (uses null check) | Yes (via `_setProperties`) |
| Can set property to null | No (null = "skip") | Yes (null is a valid value) |
| Collection strategy | Derives `Sync` — what is not sent is removed | Derives `AddOnly` — nothing is removed |
| Modes (Create/Update/Delete) | Yes | No (always Update) |
| MutationInvoker pipeline | Yes | No (manual apply) |

**When to use which:**

- **`Mutation<TEntity>` + `[Mutation]`** — Most cases. Handles the full lifecycle (create, update, delete, restore) with generated invoker pipeline.
- **`[Patch<T>]`** — When you need true PATCH semantics: distinguish "not sent" from "sent as null". Typically for public APIs.

## Collection Strategies

`[Patch<T>]` supports collection strategies via the same attributes:

```csharp
[Patch<Order>]
public partial class PatchOrder
{
    public string? OrderNumber { get; set; }

    [CollectionStrategy(CollectionStrategy.Replace)]
    public List<PatchLineItem>? LineItems { get; set; }
}

[Patch<LineItem>]
public partial class PatchLineItem
{
    public decimal? Price { get; set; }
    public int? Quantity { get; set; }
}
```

### Strategies

You do not declare one. A patch is a *partial* representation, so a collection it carries derives
`AddOnly`: what is there is updated, what is new is added, and nothing is removed — a child the caller
did not mention is a child the caller said nothing about.

Elements are matched by the element DTO's `Id`, or failing that by the child entity's `[LogicKey]`.
With neither there is nothing to match on, and the generator reports **PRAG2203** rather than guessing.

`[CollectionStrategy]` overrides the derived value where the default reads the operation wrong:

```csharp
[CollectionStrategy(CollectionStrategy.Sync)]      // this patch really does settle the whole set
public List<OrderLineDto>? Lines { get; init; }
```

| `CollectionStrategy` | Behaviour |
|---|---|
| `AddOnly` | Update by key, add what is new, remove nothing — the derived default here |
| `Sync` | Also removes what was not sent |
| `Replace` | Discard every child and rebuild — new rows, new identities |
| `Ignore` | Leave the collection alone |

## Usage with JSON Deserialization

The generator emits a `System.Text.Json` converter next to `ApplyPatch` — `{Type}.PatchJsonConverter`,
declared on the type with `[JsonConverter]` — that calls `MarkSet()` for every property the body names.
A property sent as `null` is marked and cleared; a property the body does not mention is not marked and
keeps whatever the entity has. Nothing to register: the attribute is honoured by whatever options read
the body, including the endpoint pipeline.

```csharp
// Endpoint usage
app.MapPatch("/users/{id}", async (
    Guid id,
    UpdateUserProfilePatch patch,
    IRepository<UserProfile> repo,
    IServiceProvider services,
    CancellationToken ct) =>
{
    var user = await repo.GetByIdAsync(id, ct);
    if (user is null) return Results.NotFound();

    patch.ApplyPatch(user);  // Only modifies explicitly set properties

    var uow = services.GetRequiredKeyedService<IUnitOfWork>(typeof(ProfileBoundary));
    await uow.SaveChangesAsync(ct);

    return Results.Ok();
});
```

⚠️ **A patch built in code, not read from JSON, marks nothing.** With `_setProperties` empty
`ApplyPatch` falls back to a null check — the `[Mutation]` semantics — so call `MarkSet()` yourself
for the properties you mean to write, `null` included. Property names are matched as declared and in
camelCase; `[JsonPropertyName]` is not read.

**The other tri-state form is `[GeneratePatch<TEntity>]`** (the separate `Pragmatic.Patch` package):
you declare an empty partial record and it generates the whole DTO with `Optional<T>` properties,
`ApplyTo(entity)`, `GetModifiedProperties()` and its own JSON converter. See
`Pragmatic.Patch/docs/tri-state-semantics.md`.
