---
title: "Engines: EF Core, Sql, documents"
description: "A boundary's entities are written once, with Pragmatic's attributes. Which engine stores them is the boundary's"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Persistence/docs/21-engines.md
sidebar:
  order: 22
---
A boundary's entities are written once, with Pragmatic's attributes. Which engine stores them is the boundary's
choice: EF Core, the Sql engine, or the documents engine (MongoDB, Azure Cosmos DB). The entity, its repository
contract, its actions and its endpoints are the same on all three. This page covers what changes when the engine
is not EF Core.

## When to choose which

- **EF Core** is the default and the most complete: `IQueryable`, every query strategy, EF Core migrations.
- **Sql engine** when the statements must be known at build time. Each LINQ chain is translated to SQL by the
  generator, nothing builds an expression tree per call, and the path is AOT-friendly. It runs on PostgreSQL,
  SQL Server, SQLite, MySQL and Oracle.
- **Documents engine** when an aggregate reads and writes as a whole: one document per entity, on MongoDB or
  Cosmos DB, its chains translated at build time as well.

Two boundaries of one host can use two engines (an EF Core catalog and a documents archive, for example).

## Putting a boundary on an engine

Reference the engine's bridge and name it on the boundary:

| Engine | Package | Marker |
|---|---|---|
| Sql | `Pragmatic.Persistence.Sql` | `[PersistenceEngine(PersistenceEngineKind.Sql)]` |
| Documents | `Pragmatic.Persistence.Documents` | `[PersistenceEngine(PersistenceEngineKind.Documents)]` |

```csharp
[Boundary]
[PersistenceEngine(PersistenceEngineKind.Sql)]
public partial class ReportingBoundary;
```

The marker is optional while the compilation references one engine. With two or more, a boundary that names none
is `PRAG0743`, and naming an engine the compilation does not reference is `PRAG0744` ([`[PersistenceEngine]`](/modules/persistence/03-attributes/#persistenceengine)).

A relation between entities on different engines keeps only the key. Across boundaries that is already what a relation
generates; `[ReadAccess<T>]` for an entity on another engine would add a navigation no engine can join, and a
many-to-many has no key to keep, so both are `PRAG0745`. Store the other side's id, and read that side through its own
boundary.

## Registering the session

The generator writes `Add{Boundary}Persistence` for each boundary an engine serves. It registers the boundary's
unit of work (keyed by the boundary's type), its session, and each entity's `IRepository<T>` and
`IReadRepository<T>`. The session is the application's to open, because only the application knows its
connection:

```csharp
services.AddReportingBoundaryPersistence((sp, filters) =>
    new SqlSession(new NpgsqlConnection(connectionString), SqlDialect.PostgreSql) { Filters = filters });

services.AddArchiveBoundaryPersistence((sp, filters) =>
    new DocSession(mongoClient, "archive") { Filters = filters });
```

- The factory runs once per scope (a request), with the filters the generator built from the request's services:
  tenant, current user, soft delete, data scopes. Passing them on as `Filters` is what applies them.
- A `SqlSession` owns its connection and disposes it with the scope.
- With Pragmatic.MultiTenancy referenced, the unit of work reads `MultiTenancyOptions.RequireTenant` (on by default, as on EF
  Core): a tenant's entity written with no tenant resolved is refused with `TenantNotResolvedException`.
- In a Pragmatic host the call goes in the `configure` callback of `PragmaticApp.RunAsync`, or anywhere the
  host's services are registered.

## Schema

- **Sql engine.** A database whose boundaries the engine serves gets the generated `{Database}Schema`, with the
  same tables EF Core's branch would describe. A host that references Pragmatic.Migrations migrates it at start, as
  it migrates an EF Core database, and on demand. On SQL Server, MySQL and Oracle a binary with `[MaxLength(n)]`
  is a binary of that length, so an index can hold it.
- **Documents engine.** A boundary gets the generated `{Boundary}DocSchema`: its collections, their indexes, their
  Cosmos DB partition keys and composite indexes. A Pragmatic host applies it at start through the boundary's
  session. What the backend cannot apply stops the start, and so does a boundary whose session nothing
  registered: the message names the `Add{Boundary}Persistence` call that is missing.

## What is the same

These work on every engine, from the same declarations:

- the repository's reads and writes, specifications, and the unit of work's save, with its stamps (created,
  modified, owner, tenant) and soft delete;
- the query filters: soft delete, tenant, owner, data scopes, permission-based visibility;
- declared queries (`[Query]`), the generated actions and endpoints, and `[LoadEntity]` loads;
- the traits (`[HasComments]`, `[HasNotes]`, `[HasTags]`, `[HasAttachments]`) with their actions and the
  attachments' purge;
- the bulk operations of `IBulkRepository<T>`, compiled at the call site;
- privacy's erasure and access over the entities the engine persists;
- an owned type (`[Owned]`, a `[ValueObject]` of several members, an identity record such as `LocalIdentity`, with
  the members of its base classes) is stored with its entity: as `{Property}_{Member}` columns on the Sql engine,
  as on EF Core, and as a nested document on the documents engine. A query can compare one of its members, and
  the generated `{User}.LocalIdentityStore` signs a user in through it on every engine;
- `[ConcurrencyAware]`: a stale write is a `PersistenceRuleViolationException` whose error is a
  `ConcurrencyError`, as on EF Core.

## What is different

| | Sql engine | Documents engine |
|---|---|---|
| `IReadRepository<T>.Query()` | not served (an `IQueryable` is EF Core's surface): write the chain on `SqlSession.Query<T>()`, or declare the read | not served: write the chain on `DocSession.Query<T>()`, or a specification |
| Extensions over `IQueryable` (hierarchy, temporal) | EF Core's only | EF Core's only |
| A save | one batch in one transaction | one MongoDB transaction where the server runs them (a replica set); on a standalone server and on Cosmos DB the documents are written one by one, and a failure leaves the earlier ones written |
| `[LoadEntity(Include = …)]` | compiled; a load the engine cannot compile is `PRAG3036` | not supported: `PRAG3111` |
| A mutation that writes navigations | references one-to-one: `PRAG3037`; collections and `[LinkIds]` are tracked | `PRAG3112` |
| A LINQ operator the engine cannot translate | `PRAG3001` at the call site | `PRAG3101` at the call site |

A chain the generator did not see (written in another assembly, or reached by reflection) has no plan, and its
call throws `NotSupportedException` naming why. The engines' diagnostics are `PRAG3000`–`PRAG3099` (Sql) and
`PRAG3100`–`PRAG3199` (documents), listed in the repository's diagnostics reference.

## The modules' tables

A module that keeps tables of its own (an outbox, the audit trail, the subject registry) has a package for the
engines beside its EF Core one. Reference it, and the boundary's generated `Add{Boundary}Persistence` and schema
include the module's part:

| Module | EF Core | Sql and documents | Enabled by |
|---|---|---|---|
| Event outbox | `Pragmatic.Events.EFCore` | `Pragmatic.Events.Engines` | `[EnableEventOutbox]` on the boundary |
| Messaging outbox | `Pragmatic.Messaging.EFCore` | `Pragmatic.Messaging.Engines` | `[EnableOutbox]` on the boundary |
| Audit trail | `Pragmatic.Audit.EFCore` | `Pragmatic.Audit.Engines` | `[Audited]` on an entity |
| Subject registry | `Pragmatic.Privacy.EFCore` | `Pragmatic.Privacy.Engines` | `[DataSubject]` on an entity |
| Per-subject keys (`__SubjectKeys`) | `Pragmatic.Cryptography.EFCore` | `Pragmatic.Cryptography.Engines` | `[DataSubject]` on an entity, beside the registry |

The tables are EF Core's, column for column, so a database written by both engines holds one outbox, one trail,
one registry and one set of keys. The audit trail and the subject registry are one per application, and the
keys go with the registry. The generated host registers each where it is, when one place holds it (an EF Core
database or an engine's boundary). When more than one place does, the host registers none and says so, and the
application names the one in `Program.cs`. Each module's README describes its engines section.

A `ProtectedValue` member is stored by both engines: on the Sql engine as a binary column, through
`Pragmatic.Cryptography.Engines` (PRAG3003 names the package when it is missing), on the documents engine as
Base64 text.

## Known limits

None tracked at the moment.
