---
title: "Pragmatic.Audit"
description: "An append-only audit trail whose entries can be shown not to have changed, and which survives erasing"
editUrl: https://github.com/pragmatic-design/Pragmatic.Design/edit/main/Pragmatic.Audit/README.md
sidebar:
  order: 0
  label: Overview
---
An append-only audit trail whose entries can be shown not to have changed, and which survives erasing
the people they are about.

## Why it exists

"Who did what, on whose authority" has to be answerable later, and provably unaltered, without the
trail itself becoming a store of the personal data it describes. Two properties drive the whole design:

**An entry never holds a personal value.** It identifies who and what by reference, and proves a change
with a hash rather than by keeping the old value. That is what lets the record stay complete and
verifiable after the person it concerns has been erased. There is deliberately no free-form payload
field: a field that accepts anything eventually receives everything.

**Integrity is per segment, not per entry.** Chaining each entry to the previous one would require a
total order over writes, which means serialising every write in the system. Entries are grouped into
time segments; a sealed segment gets a Merkle root, and each root chains to its predecessor.

## Packages

| Package | For |
|---------|-----|
| `Pragmatic.Audit` | The contracts: `IAuditTrail`, `ITransactionalAuditTrail`, `IAuditTrailReader`, `AuditEntry` |
| `Pragmatic.Audit.EFCore` | The EF Core store: `AuditDbContext`, `AddAuditTrail()`, sealing and retention |
| `Pragmatic.Audit.Engines` | The same trail on the Sql and documents engines: the save phase that writes the entries, and the store over a boundary's session |
| `Pragmatic.Audit.AdoNet` | The same transactional write for a producer on raw ADO.NET |
| `Pragmatic.Audit.Management` | `[UsePackage<AuditManagementPackage>]`: retention as a callable `PruneAuditTrail` action, for an application to trigger (the framework does not schedule it) |

## Quick start

In a Pragmatic host with an `[Audited]` entity there is nothing to write: the generated host registers
`AuditDbContext` and `AddAuditTrail()` on that entity's database, whose migration creates the trail's
tables. It steps aside when the application registered `AuditDbContext` itself: the one thing it
cannot decide is `[Audited]` entities in more than one database, and then the generated host says so.
Anywhere else:

```csharp
services.AddDbContext<AuditDbContext>(o => o.UseNpgsql(connectionString));
services.AddAuditTrail();
```

```csharp
await trail.RecordAsync(new AuditEntry
{
    SegmentId = string.Empty,          // assigned by the store
    OccurredAt = timeProvider.GetUtcNow(),
    Category = AuditCategory.Security,
    Operation = "Security.PermissionDenied",   // a constant, never assembled at runtime
    SubjectRef = subjectRef,                   // a pseudonym, never an identity
    Outcome = AuditOutcome.Denied,
});
```

Reading and verifying are a separate contract (`IAuditTrailReader`), because nearly everything writes,
very little should read, and reading is what an authorization policy needs to gate on its own.

```csharp
var page = await reader.QueryAsync(new AuditQuery
{
    Category = AuditCategory.Configuration,
    TenantId = tenantId,
    Limit = 50,
});

var report = await reader.VerifyAsync(from, until);
if (!report.IsIntact)
    logger.TrailBroken(report.BrokenSegmentIds);   // naming the segment is the point
```

## Where the tables come from

The trail is three tables (`__AuditEntries`, `__AuditSegments` and `__PrunedRanges`) and they are only
meaningful together: an entry whose segment row is missing cannot be sealed, so it can be written and
never checked.

- **With Pragmatic.Migrations**, nothing to do: when an entity in a database is `[Audited]`, that
  database's generated schema contains them and the runner creates them.
- **With EF Core migrations**, call `AuditDbContext.ApplyAuditConfigurations(modelBuilder)` from the
  context that should hold them, then add a migration.
- **With neither**: a producer on raw ADO.NET, such as the configuration store: the dialect carries
  the DDL: `IAuditSqlDialect.CreateSchema`.

The `__` prefix is what keeps them safe beside an application's own tables: the schema differ never drops
a framework-prefixed table for being absent from the desired schema.

## On the Sql and documents engines

A boundary the Sql or documents engine persists audits its `[Audited]` entities with the same attribute;
its project references `Pragmatic.Audit.Engines`. The generated `Add{Boundary}Persistence` registers, for
that boundary only, the phase that turns each write into an entry in the same save:

| | Sql engine | Documents engine |
|---|---|---|
| Capture | `SqlAuditPhase`: the entries in the save's batch, its transaction | `DocAuditPhase`: the entries written through the save's session, in its transaction where MongoDB runs one |
| Store | `__AuditEntries`, `__AuditSegments`, `__AuditPrunedRanges`: EF Core's tables column for column, in the database's generated schema | the same three collections, in the boundary's `{Boundary}DocSchema` |
| Trail | `AddSqlAuditTrail<TBoundary>()` | `AddDocAuditTrail<TBoundary>()` |

The trail (`IAuditTrail`, `IAuditTrailReader`, the sealer, the retention and `AuditSealingWorker`) is
registered once per application, on the session of the boundary it is read from. The generated host
follows EF Core's rule: when one place holds the `[Audited]` entities, whether an EF Core database or an
engine's boundary, it registers the trail there, after `Program.cs`'s `configure` callback, so a trail the
application registered itself is the one it keeps. When more than one place holds them, the host registers
none and says so in `RegisterAuditTrail`, and the application names the one to read from. Two boundaries count as two
places even when they open the same database: the session is the application's to open, so the host
cannot tell.

Where they differ from EF Core:

- **On the documents engine, entries of the same instant have no write order.** An entry's `Seq` is its
  instant in UTC ticks and its `Id` breaks the tie, so a segment's entries are sealed and verified in
  `(Seq, Id)` order without writers coordinating. Two entries written in the same tick come out ordered
  by `Id`, not by which was written first. On the Sql engine `Seq` is an identity column, as on EF Core.
- **On a standalone MongoDB server and on Cosmos DB** a save is not one transaction: the entries are
  written after the entities, and a failure in between leaves the entities written and their entries
  missing.
- **The package does not bring the engines' bridges.** The application references the bridge its
  boundaries use (`Pragmatic.Persistence.Sql`, `Pragmatic.Persistence.Documents`).

## Writing inside someone else's transaction

Some producers already guarantee that a change and its audit record commit together: the configuration
store and the persistence interceptor both do. `ITransactionalAuditTrail` keeps that guarantee:

```csharp
await using var transaction = await connection.BeginTransactionAsync();
await store.WriteAsync(value, transaction);
await trail.RecordAsync(entry, transaction);   // same fate as the change
await transaction.CommitAsync();
```

The cost is not hidden: enlisting means writing on the caller's connection, so the trail's tables must
live in the same database. If the trail cannot join, it **throws** rather than writing outside the
transaction: an entry that commits independently of the change it describes is worse than the missing
feature, because the trail would then disagree with the data while looking correct.

## Producers without a DbContext

`Pragmatic.Audit.AdoNet` offers the same transactional guarantee to a producer built on raw ADO.NET.
The configuration store is one: it has no EF reference at all, abstracting over three providers with
its own dialect, so it cannot hand the EF writer a context on its connection without depending on EF
*and* on one specific provider.

```csharp
var trail = new AdoNetAuditTrail(new PostgresAuditDialect(), preparer);
await trail.RecordAsync(entry, connection, transaction);
```

The dialect also carries the trail's DDL, for a producer that provisions its own schema, kept beside
the statements that read and write those tables, because those are the two things that have to agree.

## Sealing and retention

`AuditSealingService` seals segments once they are closed **plus a grace period**. The grace period is
not tuning: sealing the window that just closed would exclude writes still in flight, and the result is
a false tampering alarm, worse than no check at all, because it teaches people to ignore the real one.

`AddAuditTrail()` registers `AuditSealingWorker`, which runs it once per grace period while the host
runs. Nothing else has to be scheduled for sealing, and it matters that something does: `VerifyAsync`
checks **sealed** segments only, so a trail nobody seals reports itself intact having checked nothing.
A pass that fails is logged and retried on the next tick; several instances may each run the worker,
because the same entries under the same predecessor always seal to the same hashes.

`AuditRetentionService` discards **whole sealed segments** and records the gap as a `PrunedRange`
carrying the hash the next segment still links to. It never removes individual entries: that would
change a sealed segment's root, so retention would be indistinguishable from tampering.

## What this does not give you

Tamper-**evidence**, not tamper-proofing. Anyone with write access to the database can still alter a
row; verification reveals it, nothing here prevents it. Prevention needs database permissions or WORM
storage, which are deployment concerns.

And an integrity chain nobody verifies proves nothing: schedule `VerifyAsync`.

It is also the only trail. `Pragmatic.Logging` records that a value was redacted from a log line, and
its docs call that an audit; it is telemetry about the logging pipeline, not a record of what happened to
anyone's data, and it is not verifiable.

See the [threat model](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/security/threat-model-compliance.md).

## Status

**Functional** within 1.0.0-alpha: the append-only trail, sealing and verification, and retention; four
of the reference applications use it. See the [roadmap](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/ROADMAP.md).

## Requirements

- .NET 10.0+

## License

Part of the [Pragmatic.Design](/modules/audit/overview/) ecosystem. See [Licensing](https://github.com/pragmatic-design/Pragmatic.Design/blob/main/docs/LICENSING.md).
Pragmatic.Audit is licensed under the **PolyForm Small Business 1.0.0** license (free for small
businesses; commercial license above the threshold).
