using Pragmatic.Persistence.Documents.Cosmos;
using Pragmatic.Persistence.Documents.Mongo;
using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     A database of documents, on MongoDB or on Cosmos DB: chains start at <see cref="Query{T}" />, writes go one document at a time (or a
///     batch of new ones). With a <see cref="VersionAttribute" /> on the type, a replace or delete matches the version the object holds and every
///     successful write moves it on: a stale copy is refused with <see cref="DocConcurrencyException" />. Thread-safe as its client is.
/// </summary>
public sealed partial class DocSession
{
    private const int InsertBatch = 1000;

    public DocSession(MongoClient mongo, string database)
    {
        Mongo = mongo ?? throw new ArgumentNullException(nameof(mongo));
        Database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public DocSession(CosmosClient cosmos, string database)
    {
        Cosmos = cosmos ?? throw new ArgumentNullException(nameof(cosmos));
        Database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>The MongoDB client, when the session is on MongoDB.</summary>
    public MongoClient? Mongo { get; }

    /// <summary>The Cosmos DB client, when the session is on Cosmos DB.</summary>
    public CosmosClient? Cosmos { get; }

    public string Database { get; }

    /// <summary>The caller, for the filter families of Pragmatic's entities (<see cref="DocFamily" />); null: every family active, its values empty.</summary>
    public IDocFilters? Filters { get; init; }

    /// <summary>
    ///     A chain on the documents of <typeparamref name="T" />, a [Document] class. Unconstrained on purpose: the generator reads the chain in a
    ///     compilation without its own output, where a [Document] of the same project does not implement <see cref="IDocumentEntity{TSelf}" /> yet;
    ///     a type that is not a document is reported at the call site (PRAG3101), and the plan's execution is constrained.
    /// </summary>
    public DocQuery<T> Query<T>() => new(this);

    public Task InsertAsync<T>(T entity, CancellationToken ct = default) where T : IDocumentEntity<T> => InsertManyAsync([entity], ct);

    /// <summary>New documents: on MongoDB in batches of 1000 per <c>insert</c> (ordered: a refused one stops its batch, <see cref="MongoWriteException" />); on Cosmos DB one request each (a taken id: <see cref="CosmosException" /> 409).</summary>
    public Task InsertManyAsync<T>(IReadOnlyList<T> entities, CancellationToken ct = default) where T : IDocumentEntity<T>
        => Cosmos is not null ? CosmosInsertManyAsync(entities, ct) : MongoInsertManyAsync(entities, ct);

    /// <summary>The stored document replaced by <paramref name="entity" />; versioned: only the version it holds, then one more.</summary>
    public Task ReplaceAsync<T>(T entity, CancellationToken ct = default) where T : IDocumentEntity<T>
        => Cosmos is not null ? CosmosReplaceAsync(entity, upsert: false, ct) : MongoReplaceAsync(entity, ct);

    /// <summary>Inserted when the key is new, replaced when it is there; versioned: replaced only at the version the object holds (a stale copy is refused).</summary>
    public Task UpsertAsync<T>(T entity, CancellationToken ct = default) where T : IDocumentEntity<T>
        => Cosmos is not null ? CosmosReplaceAsync(entity, upsert: true, ct) : MongoUpsertAsync(entity, ct);

    /// <summary>The stored document deleted; versioned: only at the version the object holds.</summary>
    public Task DeleteAsync<T>(T entity, CancellationToken ct = default) where T : IDocumentEntity<T>
        => Cosmos is not null ? CosmosDeleteAsync(entity, ct) : MongoDeleteAsync(entity, ct);

    internal MongoClient MongoClient => Mongo ?? throw new InvalidOperationException("The session is on Cosmos DB.");

    private async Task MongoInsertManyAsync<T>(IReadOnlyList<T> entities, CancellationToken ct) where T : IDocumentEntity<T>
    {
        ArgumentNullException.ThrowIfNull(entities);
        for (var start = 0; start < entities.Count; start += InsertBatch)
        {
            var end = Math.Min(start + InsertBatch, entities.Count);
            await RunAsync(w =>
            {
                w.Write("insert", T.Collection);
                w.StartArray("documents");
                for (var i = start; i < end; i++)
                {
                    w.StartDocument(i - start);
                    T.WriteFields(w, entities[i]);
                    w.EndDocument();
                }

                w.EndArray();
            }, ct).ConfigureAwait(false);
        }
    }

    private async Task MongoReplaceAsync<T>(T entity, CancellationToken ct) where T : IDocumentEntity<T>
    {
        var version = T.Versioned ? T.GetVersion(entity) : 0;
        if (T.Versioned)
            T.SetVersion(entity, version + 1);
        try
        {
            var reply = await Update(entity, version, upsert: false, ct).ConfigureAwait(false);
            if (!reply.TryGetNumber("n", out var matched) || matched == 0)
                throw new DocConcurrencyException(T.Versioned
                    ? $"{T.Collection}: no document with this key at version {version} (changed or deleted since it was read)."
                    : $"{T.Collection}: no document with this key.");
        }
        catch
        {
            if (T.Versioned)
                T.SetVersion(entity, version); // the object keeps the version it had: the write did not happen
            throw;
        }
    }

    private async Task MongoUpsertAsync<T>(T entity, CancellationToken ct) where T : IDocumentEntity<T>
    {
        var version = T.Versioned ? T.GetVersion(entity) : 0;
        if (T.Versioned)
            T.SetVersion(entity, version + 1);
        try
        {
            await Update(entity, version, upsert: true, ct).ConfigureAwait(false);
        }
        catch (MongoWriteException e) when (e.IsDuplicateKey && T.Versioned)
        {
            // The key is there at another version: the upsert's filter (key and version) missed and its insert met the key.
            T.SetVersion(entity, version);
            throw new DocConcurrencyException($"{T.Collection}: the document with this key is not at version {version}.");
        }
        catch
        {
            if (T.Versioned)
                T.SetVersion(entity, version);
            throw;
        }
    }

    private async Task MongoDeleteAsync<T>(T entity, CancellationToken ct) where T : IDocumentEntity<T>
    {
        var reply = await RunAsync(w =>
        {
            w.Write("delete", T.Collection);
            w.StartArray("deletes");
            w.StartDocument(0);
            Filter(w, entity, T.Versioned ? T.GetVersion(entity) : 0);
            w.Write("limit", 1);
            w.EndDocument();
            w.EndArray();
        }, ct).ConfigureAwait(false);
        if (T.Versioned && (!reply.TryGetNumber("n", out var deleted) || deleted == 0))
            throw new DocConcurrencyException($"{T.Collection}: no document with this key at version {T.GetVersion(entity)}.");
    }

    private Task<BsonDocument> Update<T>(T entity, long version, bool upsert, CancellationToken ct) where T : IDocumentEntity<T>
        => RunAsync(w =>
        {
            w.Write("update", T.Collection);
            w.StartArray("updates");
            w.StartDocument(0); // the update statement: q (key and version), u (the replacement document), upsert
            Filter(w, entity, version);
            w.StartDocument("u");
            T.WriteFields(w, entity);
            w.EndDocument();
            if (upsert)
                w.Write("upsert", true);
            w.EndDocument();
            w.EndArray();
        }, ct);

    /// <summary>The <c>q</c> of an update or delete: the key, and the version when the type has one.</summary>
    private static void Filter<T>(BsonWriter w, T entity, long version) where T : IDocumentEntity<T>
    {
        w.StartDocument("q");
        T.WriteKey(w, "_id", entity);
        if (T.Versioned)
            w.Write(DocVersion.Field, version);
        w.EndDocument();
    }
}
