# Pragmatic.Persistence.Documents samples

Document databases with the chains translated at compile time, on MongoDB (Pragmatic's driver: wire protocol, BSON, SCRAM-SHA-256, TLS)
and on Azure Cosmos DB (its REST API: master-key signatures, JSON). No SDK of either, no reflection: the same code publishes Native AOT
with every trim and AOT warning an error. Each sample checks what it got against the seed; the program exits 0 when every check held.

```
PRAGMATIC_SAMPLES_MONGO="mongodb://user:password@localhost:27017/?authSource=admin" dotnet run --project Pragmatic.Persistence/samples/Pragmatic.Persistence.Documents.Samples
PRAGMATIC_SAMPLES_COSMOS="AccountEndpoint=http://localhost:8081/;AccountKey=…;" dotnet run …   (Cosmos DB, the emulator or an account)
dotnet publish … -c Release -r win-x64 -p:PragmaticAotSmokeRestore=true                          (Native AOT)
```

The gate runs them in `Pragmatic.Persistence.Documents.Samples.Tests`, on a standalone MongoDB 8 and on the Cosmos DB emulator in containers:
42 checks on MongoDB, 40 on Cosmos DB, every one of them required to hold.

The model is the aggregate: an order is one document with its lines inside (`[Embedded]`), nothing refers to another document. The classes are
`partial`; the generator adds their BSON and JSON readers and writers. `[Key]` is `_id` (MongoDB) or `id` (Cosmos DB), `[Version]` is checked
and moved on by every write (`DocConcurrencyException` for a stale copy). A chain's values go in the two-parameter form
(`Where((o, id) => o.CustomerId == id, id)`; several as a tuple); a captured local is PRAG3102 at build.

| Feature | File |
|---|---|
| A filtered, ordered page; Count, Any, FirstOrDefault, Max, Min, Sum; a projection (the Select runs in .NET, the document read with the fields it names); a list of values (`$in`, `ARRAY_CONTAINS`); a text match (the text as it is); keyset pagination (`After`); conditions on an embedded array's elements, an array of values, an embedded document and its absence; another collection | `Orders/OrderSamples.cs` |
| Insert, replace at the version, a stale copy refused, upsert, delete | `Orders/OrderWrites.cs` |
| MongoDB only: `GroupBy(key).Select(g => new { g.Key, Count = g.Count(), Total = g.Sum(…) })` (one `$group`); `ExecuteUpdateAsync(s => s.Set(…))` and `ExecuteDeleteAsync()` by filter (one command each, the version moved on) | `Orders/Pipelines.cs` |
| `SelectMany(o => o.Lines)` flattens an embedded list (`$unwind`; on Cosmos DB `JOIN u IN c.Lines`); `Join(session.Query<TInner>()…, keys, result)` joins another collection (`$lookup`; on Cosmos DB a second query for the keys, joined in .NET); on MongoDB a `Where` over the groups (HAVING) | `Orders/Joins.cs` |
| Pragmatic's entities (`[Entity]`, `[Relation.*]`, soft delete, owner, scopes, a tenant, `[VisibleWhen]`; `Entities/`): the schema applied at a host's start, the filter families per caller and mode (`PragmaticDocFilters`), a join whose inner side keeps them, a count across Cosmos DB's partitions, the generated `{Entity}.DocReadRepository` with specifications compiled where they are written | `Flows/EntitySamples.cs` |
| Pragmatic's declared queries and grids: a paged `[Query]` projected into a DTO, inside the caller's filters and Raw; a DevExpress grid's page, its filter as a request body holds it (`SampleJson`, no reflection) | `Flows/QuerySamples.cs` |
| The other filter families: the temporal filter and `IncludeHistory`, a computed and a materialized scope, a trait's child visible where its parent is, a tree's descendants | `Flows/FamilySamples.cs` |
| Pragmatic's unit of work (`DocUnitOfWork`, the generated `{Entity}.DocRepository`): load, change, save with the audit and the soft delete, a versioned document (`[ConcurrencyAware]`), the second of two writers refused; this MongoDB is standalone, so the save runs without a transaction | `Flows/UnitOfWorkSamples.cs` |

Multi-document transactions (`session.BeginTransaction()`, then `CommitAsync`, or dispose to abort) need a replica set: they are covered by
the tests (`DocumentTransactionTests`, `mongo:8 --replSet`), not here (the samples' server is standalone). Cosmos DB refuses a GroupBy, the
commands by filter (it writes by id) and the transactions, with the reason.

Cosmos DB's gateway serves ORDER BY, OFFSET/LIMIT, COUNT and aggregates in one partition only: the chains here name the customer (the orders'
`PartitionKey`), so they run as they do on MongoDB. Across partitions a filter and Any run; a count and an ordered read run once per partition
key range, counted and merged (`OrderSamples.CosmosLimitsAsync`); an aggregate is refused before it is sent, with the remedy. The generator
notes what needs the partition at the call site (PRAG3106, information). An ORDER BY on several keys needs a composite index on the container
(`EnsureContainerAsync` takes them). A chain that names the key and the partition key and nothing else
(`FirstOrDefaultAsync((o, k) => o.CustomerId == k.Customer && o.Id == k.Id, …)`) is a point read: a GET of the document, not a query. Cosmos
DB keeps numbers as doubles: a decimal is exact up to 15 significant digits, a Sum is a sum of doubles. MongoDB keeps instants to the
millisecond.
