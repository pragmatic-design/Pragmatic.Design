# Pragmatic.Audit.AdoNet

Writes trail entries with raw ADO.NET, on a connection and transaction the caller already owns.

## Why it exists

The EF writer cannot reach every producer that needs atomicity. A producer built on ADO.NET with its
own dialect over several providers — `Pragmatic.Configuration.Database` is exactly that — cannot hand
the EF writer a `DbContext` on its connection without depending on EF *and* on one specific provider,
inside a package written to abstract over providers.

That was discovered by measuring, after the enlisting contract had already been built for that very
producer.

## Use

```csharp
var trail = new AdoNetAuditTrail(new PostgresAuditDialect(), preparer);

await using var transaction = await connection.BeginTransactionAsync();
await store.WriteAsync(value, transaction);
await trail.RecordAsync(entry, connection, transaction);   // same fate as the change
await transaction.CommitAsync();
```

Entry shaping goes through `AuditEntryPreparer`, the same as every other writer. Skipping it produces
entries with no segment — which cannot be sealed, and therefore cannot be verified — and with an
unredacted detail.

## The dialect

Only what genuinely differs between providers: the segment upsert. Two writers opening the same segment
in the same instant is the ordinary case, not an edge one, so a writer that treats "somebody else
created it first" as an error turns contention into failed audit writes.

`CreateSchema` is here too, beside the statements that read and write those tables, because those are
the two things that have to agree. A column added to the DDL in one package and to the `INSERT` in
another is the drift that surfaces as a runtime error long after the change.

⚠️ A losing race on the segment insert is swallowed **only outside a transaction**. Inside one the
failure has already poisoned it, and hiding that would commit an entry into a segment never opened.
