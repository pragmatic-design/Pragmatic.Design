# The documents engine

A boundary whose aggregates read and write as a whole can be stored as documents, on MongoDB or Azure
Cosmos DB. The engine translates LINQ chains at compile time into the backend's query: no expression
tree per call, no reflection.

## Packages

| Package | What it is |
|---|---|
| `Pragmatic.Persistence.Documents` | the bridge: serves Pragmatic's `[Entity]` types on the engine, with its unit of work, filters and specifications. An application references this one |
| `Pragmatic.Persistence.Documents.Core` | the engine itself: `DocSession`, `DocQuery<T>`, plans, schema, its own attributes. Comes with the bridge |
| `Pragmatic.Persistence.Documents.Mongo`, `….Cosmos` | the engine's own MongoDB and Cosmos DB clients. Come with the engine |

The namespaces carry no `.Core`: the engine's types are in `Pragmatic.Persistence.Documents` and its
sub-namespaces (`.Attributes`, `.Filters`, `.Planning`, `.Schema`, …).

⚠️ None of these packages is in 1.0.0-alpha.3: the engine ships with a later release.

## Pragmatic's entities on the engine

A boundary on the engine keeps its `[Entity]` types, repositories, actions and endpoints. The host opens
the session, and the generated `Add{Boundary}Persistence` registers the rest:

```csharp
services.AddArchiveBoundaryPersistence((sp, filters) =>
    new DocSession(mongoClient, "archive") { Filters = filters });
```

- The factory runs once per scope. Pass `filters` on: they carry tenant, owner, soft delete and scopes.
- The boundary's generated `{Boundary}DocSchema` (collections, indexes, Cosmos DB partition keys) is
  applied at start through that session. What the backend cannot apply stops the start.
- A save is one MongoDB transaction on a replica set. On a standalone server and on Cosmos DB the
  documents are written one by one, and a failure leaves the earlier ones written.
- `IReadRepository<T>.Query()` throws: write the chain on `DocSession.Query<T>()`, or a specification.
- A load with `Include` is **PRAG3111**; a mutation that loads or writes navigations is **PRAG3112**.
- An owned type (`[Owned]`, a `[ValueObject]` of several members, an identity record such as
  `LocalIdentity`) is a nested document of its entity, with its base classes' members. It needs no
  `[Embedded]`: the generator writes its reader and writer. A chain can compare one of its members.
- A module's collections come from its engines package: `Pragmatic.Events.Engines`,
  `Pragmatic.Messaging.Engines`, `Pragmatic.Audit.Engines`, `Pragmatic.Privacy.Engines`.

The full list of what is the same as EF Core and what is not is the module's guide, `docs/21-engines.md`.

## Choosing the engine for a boundary

```csharp
[Boundary]
[PersistenceEngine(PersistenceEngineKind.Documents)]     // Pragmatic.Persistence.Entity
public partial class ReviewsBoundary;
```

Optional while the compilation references one engine. With two or more, a boundary that names none is
**PRAG0743**; naming an engine the compilation does not reference is **PRAG0744**. A relation to an entity on
another engine keeps only its key: `[ReadAccess<T>]` for that entity, or a many-to-many with it, is **PRAG0745**.

## The engine's own attributes

These are in `Pragmatic.Persistence.Documents.Attributes`, for a type the engine stores without going
through Pragmatic's `[Entity]`.

| Attribute | On | What it does |
|---|---|---|
| `[Document("collection")]` | `partial` class | an aggregate stored as one document of that collection; `PartitionKey = nameof(…)` names the property it is partitioned by (Cosmos DB's partition key, a shard key), and a chain that names it in an equality stays in one partition |
| `[Embedded]` | `partial` class | a type stored inside its document: a property of that type is a nested document, a list of it an array. Nothing refers to another document |
| `[Key]` | property | the document's identity, stored as `_id` |
| `[Version]` | integer property | checked and incremented by the writes: a replace or delete of a stale copy is refused with `DocConcurrencyException` |

```csharp
using Pragmatic.Persistence.Documents.Attributes;

[Document("orders", PartitionKey = nameof(CustomerId))]
public sealed partial class OrderDocument
{
    [Key] public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
    [Version] public int Version { get; set; }
}

[Embedded]
public sealed partial class OrderLine
{
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
}
```

⚠️ The attributes share their short names with others (`Key`, `Version`, and an application type called
`Document`): C# picks the one that is an attribute when only one is, so the `using` above is what makes
`[Document]` and `[Key]` mean the engine's.
