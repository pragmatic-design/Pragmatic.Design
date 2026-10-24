using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pragmatic.Persistence.Documents.Cosmos;
using Pragmatic.Persistence.Documents.Mongo;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     The writes a unit of work makes of what it tracks. On MongoDB a changed document is a <c>$set</c> of its changed fields only, at the version
///     the object holds: a field another writer changed in between is left as that writer wrote it. On Cosmos DB a document is replaced whole,
///     so it is written with <c>If-Match</c> on the etag its read returned (the session keeps it beside the object): one request, and a document
///     changed in between is refused, versioned or not (owner's decision of 2026-10-22).
/// </summary>
public sealed partial class DocSession
{
    private static readonly ConditionalWeakTable<MongoClient, Task<bool>> Topologies = new();
    private readonly ConditionalWeakTable<object, string> _etags = new();

    /// <summary>The etag of the document <paramref name="entity" /> was read from or last written as, on Cosmos DB; null when the session has none.</summary>
    public string? ETagOf(object entity) => _etags.TryGetValue(entity, out var etag) ? etag : null;

    /// <summary>A document read on Cosmos DB: its etag kept beside the object it became.</summary>
    internal void Remember(object? entity, JsonElement document)
    {
        if (entity is not null && document.TryGetProperty("_etag", out var etag) && etag.ValueKind == JsonValueKind.String)
            _etags.AddOrUpdate(entity, etag.GetString()!);
    }

    /// <summary>A document written on Cosmos DB: the etag the write returned kept beside the object.</summary>
    private void Remember(object? entity, CosmosResponse written)
    {
        if (entity is not null && (int)written.Status < 300 && written.ETag is { } etag)
            _etags.AddOrUpdate(entity, etag);
    }

    /// <summary>
    ///     True when the server runs multi-document transactions: MongoDB on a replica set or behind <c>mongos</c>, as its <c>hello</c> says (asked
    ///     once per client). A standalone MongoDB and Cosmos DB do not.
    /// </summary>
    public Task<bool> SupportsTransactionsAsync(CancellationToken ct = default)
        => Mongo is not { } mongo ? Task.FromResult(false) : Topologies.GetValue(mongo, client => AskTopologyAsync(client, ct));

    private static async Task<bool> AskTopologyAsync(MongoClient client, CancellationToken ct)
    {
        var hello = await client.RunCommandAsync("admin", w => w.Write("hello", 1), ct).ConfigureAwait(false);
        return hello.TryGetString("setName", out var set) && set.Length > 0 || hello.TryGetString("msg", out var msg) && msg == "isdbgrid";
    }

    /// <summary>
    ///     MongoDB: the fields of <paramref name="mask" /> set (<see cref="IDocumentTracked{TSelf}.WriteSet" />), the document matched by its key and,
    ///     versioned, at the version the object holds, which moves on by one. No document matched: <see cref="DocConcurrencyException" />.
    /// </summary>
    public async Task UpdateFieldsAsync<T>(T entity, ulong mask, CancellationToken ct = default) where T : IDocumentTracked<T>
    {
        if (Cosmos is not null)
            throw new NotSupportedException("Cosmos DB replaces a document whole: use ReplaceTrackedAsync.");
        if (mask == 0 && !T.Versioned)
            return; // nothing to set
        var version = T.Versioned ? T.GetVersion(entity) : 0;
        var reply = await RunAsync(w =>
        {
            w.Write("update", T.Collection);
            w.StartArray("updates");
            w.StartDocument(0);
            Filter(w, entity, version);
            w.StartDocument("u");
            w.StartDocument("$set");
            T.WriteSet(w, entity, mask);
            if (T.Versioned)
                w.Write(DocVersion.Field, version + 1);
            w.EndDocument();
            w.EndDocument();
            w.EndDocument();
            w.EndArray();
        }, ct).ConfigureAwait(false);
        if (!reply.TryGetNumber("n", out var matched) || matched == 0)
            throw new DocConcurrencyException(T.Collection, T.Versioned
                ? $"{T.Collection}: no document with this key at version {version} (changed or deleted since it was read)."
                : $"{T.Collection}: no document with this key.");
        // The object moves on only once the write is done: a failed one leaves it at the version it was read at.
        if (T.Versioned)
            T.SetVersion(entity, version + 1);
    }

    /// <summary>
    ///     Cosmos DB: the document replaced with <c>If-Match</c> on the etag the session keeps for <paramref name="entity" /> (one request; 412 or 404:
    ///     <see cref="DocConcurrencyException" />). Without one (an object never read here) the replace checks the version first, as
    ///     <see cref="ReplaceAsync{T}" /> does.
    /// </summary>
    public async Task ReplaceTrackedAsync<T>(T entity, CancellationToken ct = default) where T : IDocumentEntity<T>
    {
        if (Cosmos is null)
            throw new NotSupportedException("MongoDB sets the changed fields: use UpdateFieldsAsync.");
        if (ETagOf(entity!) is not { } etag)
        {
            await ReplaceAsync(entity, ct).ConfigureAwait(false);
            return;
        }

        var version = T.Versioned ? T.GetVersion(entity) : 0;
        if (T.Versioned)
            T.SetVersion(entity, version + 1);
        try
        {
            var written = await Send(HttpMethod.Put, entity, etag, upsert: false, ct, HttpStatusCode.PreconditionFailed, HttpStatusCode.NotFound).ConfigureAwait(false);
            if (written.Status is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
                throw new DocConcurrencyException(T.Collection, $"{T.Collection}: the document with this key changed or was deleted since it was read.");
        }
        catch
        {
            if (T.Versioned)
                T.SetVersion(entity, version);
            throw;
        }
    }

    /// <summary>
    ///     The document deleted; on Cosmos DB with <c>If-Match</c> on the etag the session keeps (a document changed since: refused, versioned or not),
    ///     without one as <see cref="DeleteAsync{T}" />.
    /// </summary>
    public async Task DeleteTrackedAsync<T>(T entity, CancellationToken ct = default) where T : IDocumentEntity<T>
    {
        if (Cosmos is null || ETagOf(entity!) is not { } etag)
        {
            await DeleteAsync(entity, ct).ConfigureAwait(false);
            return;
        }

        var deleted = await Send(HttpMethod.Delete, entity, etag, upsert: false, ct, HttpStatusCode.PreconditionFailed, HttpStatusCode.NotFound).ConfigureAwait(false);
        if (deleted.Status != HttpStatusCode.NoContent)
            throw new DocConcurrencyException(T.Collection, $"{T.Collection}: the document with this key changed or was deleted since it was read.");
        _etags.Remove(entity!);
    }
}
