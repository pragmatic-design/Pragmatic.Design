# Mutations

## The Problem

Updating an entity from a DTO is surprisingly tricky. Consider a typical scenario:

```csharp
// The naive approach — manual property mapping
public async Task UpdateOrder(Guid id, UpdateOrderRequest request, CancellationToken ct)
{
    var order = await repo.GetByIdAsync(id, ct);
    if (order is null) return NotFound();

    // You must manually map every nullable property
    if (request.Total.HasValue) order.SetTotal(request.Total.Value);
    if (request.Status.HasValue) order.SetStatus(request.Status.Value);
    if (request.ShippingAddress is not null) request.ShippingAddress.ApplyToEntity(order.ShippingAddress);

    // And what does sending a collection mean? Replace it? Merge into it? Add to it?
    if (request.Items is not null)
    {
        // This is where it gets really messy...
        foreach (var item in request.Items) { /* merge logic */ }
    }

    await uow.SaveChangesAsync(ct);
}
```

For every DTO, you write the same `if (property.HasValue)` pattern. For collections, you implement merge/replace/append logic. With 30 update DTOs, this becomes thousands of lines of error-prone mapping code.

## The Solution: `Mutation<TEntity>` + `[Mutation]`

A **mutation** is a class that inherits from `Mutation<TEntity>` and is marked with `[Mutation]`. You declare the properties you want to update, and the source generator produces the `ApplyToEntity()` method.

```csharp
// ═══ What YOU write ═══
[Mutation(Mode = MutationMode.Update)]
public partial class UpdateOrderDto : Mutation<Order>
{
    public decimal? Total { get; init; }
    public OrderStatus? Status { get; init; }
}
```

```csharp
// ═══ What the SG generates ═══
public partial class UpdateOrderDto
{
    public override void ApplyToEntity(Order entity)
    {
        if (Total.HasValue) entity.SetTotal(Total.Value);
        if (Status.HasValue) entity.SetStatus(Status.Value);
    }
}
```

### Why properties are nullable

Each property on the mutation DTO is nullable (`decimal?`, `OrderStatus?`). This means:
- **`null`** = "don't change this property" (skip it)
- **A value** = "update this property to this value"

This gives you **partial updates** — you only send the fields you want to change. This is the same concept as PATCH in REST APIs.

## Usage

```csharp
public async Task UpdateOrder(Guid id, UpdateOrderDto dto, CancellationToken ct)
{
    var order = await repo.GetByIdAsync(id, ct);
    if (order is null) return NotFound();

    dto.ApplyToEntity(order);           // One line — all mapping is generated
    await uow.SaveChangesAsync(ct);
}
```

## Writing an aggregate's children

A mutation can carry the children of its aggregate. Three things have to hold, and the generator says
so when they do not.

### 1. The child declares that it has no life of its own

`[PartOf<TParent>]` on the child entity. It is fail-closed: without it the collection is not written
and you get **PRAG0436**.

The marker is required because no relation metadata separates a line item from a room type — both are
declared `[Relation.OneToMany]`, and one of them has permissions, validation and events of its own. A
parent that wrote it anyway would be a way around all three.

```csharp
[Entity]
[PartOf<Order>]          // written through its parent, never addressed directly
public partial class OrderLine : IEntity { /* … */ }
```

Addressing a `[PartOf]` entity with a mutation of its own is **PRAG0438**.

### 2. The property is named after the navigation it writes

`[Relation.OneToMany<OrderLine>]` on `Order` produces the navigation `OrderLines`, so the mutation
property is `OrderLines`. A name that matches no navigation is **PRAG0439** — without that diagnostic
the child would be dropped in silence and the endpoint would answer 200 having written nothing.

### 3. The elements can be matched against what is already there

By the element DTO's `Id`, or failing that by the child's `[LogicKey]`. With neither, there is nothing
to match on: **PRAG0333** for a mutation, **PRAG2203** for a `[Patch]`.

```csharp
[Mutation(Mode = MutationMode.Update)]
public partial class CurateOrderLinesMutation : Mutation<Order>
{
    public required Guid Id { get; init; }
    public required List<OrderLineDto> OrderLines { get; init; }
}
```

### The strategy is derived, not declared

You do not normally write it. It follows from what the operation *is*:

| The operation | Strategy | Because |
|---|---|---|
| `[Patch]` | `AddOnly` | A patch is a partial representation: a child you did not mention is one you said nothing about |
| anything else | `Sync` | A mutation is a full representation: a child you did not send is one you are saying is not there |

`[CollectionStrategy]` overrides it where the default reads the operation wrong:

```csharp
[CollectionStrategy(CollectionStrategy.AddOnly)]
public required List<OrderLineDto> OrderLines { get; init; }
```

| `CollectionStrategy` | What it does |
|---|---|
| `Sync` | Match by key: update what is there, add what is new, **remove what was not sent** |
| `AddOnly` | Match by key: update what is there, add what is new, remove nothing |
| `Replace` | Discard every child and rebuild from what was sent — new rows, new identities |
| `Ignore` | Do not write this collection at all |

`Replace` is the one to be careful with: the rows are new, so the identity, the audit columns and
anything pointing at the old ones go with them.

### What the generator writes

```csharp
public override void ApplyToEntity(Order entity)
{
    global::Pragmatic.Mapping.Mutation.MutationHelpers.MapOneToMany(
        this.OrderLines,
        entity.OrderLines,
        d => d.Id,                    // key on the DTO side
        e => e.Id,                    // key on the entity side
        d => d.ToEntity(),            // what to do with one that has no match
        (d, e) => d.ApplyTo(e),       // what to do with one that has
        global::Pragmatic.Mapping.Mutation.CollectionStrategy.Sync);
}
```

The load brings the collection with it: a `Sync` against children nobody loaded would remove nothing
and add everything a second time.

### Removing a `[SoftDelete]` child

It is flagged, not deleted — the same as any other delete of that entity.

This used not to hold. Soft delete lived only in the generated repository, and a child taken out of a
collection never reached it: EF saw an orphan of a required relationship and removed the row, so an
entity whose `[SoftDelete]` promised recoverability lost one. It is now enforced at save time, where
every path converges — the repository, a mutation, this merge, a hand-written `context.Remove`.

Two things follow from that:

- **The stamp is not rewritten.** A cascade shares one instant and a restore returns only the children
  carrying it, so an entity already flagged is left exactly as the repository left it.
- **Erasure has to say it means it.** `SoftDeleteScope.Suspend()` steps out for the work inside it,
  which is how a GDPR erasure step removes the row rather than flagging it.

```csharp
using (SoftDeleteScope.Suspend())
{
    repository.Remove(subject);
    await unitOfWork.SaveChangesAsync(ct);   // the row is gone
}
```

⚠️ `ExecuteDelete` is translated straight to SQL and never enters the change tracker, so nothing
converts it. **PRAG0687** reports it on a `[SoftDelete]` entity rather than leaving it to be found in
production.

## Mutation Modes

Mutations can operate in different modes depending on the use case:

`[Mutation]` is **not** `AllowMultiple` — one per class. And `Mode` is optional: left unset it is
inferred from the class name, so `CreateOrder` is a Create and `UpdateOrder` an Update.

```csharp
[Mutation]                                    // Mode inferred: Create
public partial class CreateOrder : Mutation<Order> { /* ... */ }

[Mutation(Mode = MutationMode.Restore)]       // say it when the name does not
public partial class ReopenOrder : Mutation<Order> { /* ... */ }
```

| Mode | Generated Behavior |
|------|-------------------|
| `Create` | `new TEntity()`, applies properties, saves. **Not** the `Create()` factory — there is no factory lookup |
| `Update` | Loads by `Id`, calls `ApplyToEntity()`, saves |
| `CreateOrUpdate` | Decides at runtime: `Id` set and found → update, otherwise create |
| `Delete` | Loads by `Id` and removes it — a flag rather than a row when the entity is `[SoftDelete]` |
| `Restore` | Loads with the soft-delete filter off, resets `IsDeleted` / `DeletedAt` / `DeletedBy`, saves |

`[Mutation]` also carries `ReturnType` (`Id` — the default — `LogicalKey`, or `Entity`) and
`SoftDelete`, which only means anything on a `Delete`.

`Internal` has three states rather than two, and the useful one is leaving it unset: an operation with
an `[Endpoint]` is surface, so it lands on the boundary's public interface, and one without is a step,
so it lands on the internal one. Write `Internal = false` for the case the inference cannot see — an
operation with no HTTP surface that other modules are meant to call — and `Internal = true` to keep one
off the public interface despite its endpoint.

For `[SoftDelete]` entities, `MutationMode.Delete` automatically performs a soft-delete — you don't need to specify this explicitly.

## Nested Mutations

Mutations can reference other mutation DTOs for nested entity updates:

```csharp
[Mutation(Mode = MutationMode.Update)]
public partial class UpdateOrderDto : Mutation<Order>
{
    public required Guid Id { get; init; }
    public decimal? Total { get; init; }
    public UpdateAddressDto? ShippingAddress { get; init; }
}

[Mutation(Mode = MutationMode.Update)]
public partial class UpdateAddressDto : Mutation<Address>
{
    public string? Street { get; init; }
    public string? City { get; init; }
}
```

```csharp
// ═══ Generated ApplyTo ═══
public void ApplyToEntity(Order target)
{
    if (Total.HasValue) target.SetTotal(Total.Value);
    ShippingAddress?.ApplyToEntity(target.ShippingAddress);     // Nested apply
}
```

## Generated MutationInvoker

Every valid mutation gets one — `[Endpoint]` is not a condition, it only decides whether the invoker
is also reachable over HTTP.

```csharp
// ═══ Generated: {Namespace}.UpdateOrderDto.MutationInvoker.g.cs ═══
// 1. Load the entity from the repository (Update / Delete / Restore), or construct it (Create)
// 2. Apply the mutation via ApplyToEntity()
// 3. Save through the boundary's IUnitOfWork
// 4. Return Result<T, IError>
```

So the load-apply-save shown under "Usage" is what the invoker already does: write it yourself only
when you are calling the mutation from your own code without going through the invoker.

## Properties with `required`

Properties marked `required` are mandatory in the mutation:

```csharp
[Mutation(Mode = MutationMode.Create)]
public partial class CreateOrderDto : Mutation<Order>
{
    public required string OrderNumber { get; init; }    // Must be provided
    public required decimal Total { get; init; }         // Must be provided
    public string? SpecialNotes { get; init; }           // Optional
}
```

In `MutationMode.Create` mark `required` whatever the entity cannot be without — the invoker
constructs it with `new TEntity()` and then applies the properties, so nothing else enforces them. In
`MutationMode.Update` the properties are nullable, because there `null` means "leave it alone".
