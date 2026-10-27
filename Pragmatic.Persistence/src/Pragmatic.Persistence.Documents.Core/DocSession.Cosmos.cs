using System.Net;
using System.Text.Json;
using Pragmatic.Persistence.Documents.Cosmos;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     The writes on Cosmos DB. The REST API compares an etag, not a field: a versioned replace or delete reads the stored document, checks its
///     <c>_v</c> against the object's version, then writes with <c>If-Match</c> on the etag it read (a write in between is a 412: refused too).
///     Two requests per versioned write; an unversioned one is one request.
/// </summary>
public sealed partial class DocSession
{
    private const int Parallel = 16;

    internal CosmosClient CosmosClient => Cosmos ?? throw new InvalidOperationException("The session is on MongoDB.");

    /// <summary>The container of <typeparamref name="T" />, created when missing, partitioned as the type says, with the composite indexes an ORDER BY on several keys needs.</summary>
    public async Task EnsureContainerAsync<T>(IEnumerable<(string Path, bool Descending)[]>? compositeIndexes = null, CancellationToken ct = default) where T : IDocumentEntity<T>
    {
        await CosmosClient.CreateDatabaseIfNotExistsAsync(Database, ct).ConfigureAwait(false);
        await CosmosClient.CreateContainerIfNotExistsAsync(Database, T.Collection, T.PartitionKeyPath, compositeIndexes, ct).ConfigureAwait(false);
    }

    private async Task CosmosInsertManyAsync<T>(IReadOnlyList<T> entities, CancellationToken ct) where T : IDocumentEntity<T>
    {
        ArgumentNullException.ThrowIfNull(entities);
        for (var start = 0; start < entities.Count; start += Parallel)
        {
            var wave = Enumerable.Range(start, Math.Min(Parallel, entities.Count - start)).Select(i => Send(HttpMethod.Post, entities[i], null, upsert: false, ct));
            await Task.WhenAll(wave).ConfigureAwait(false);
        }
    }

    private async Task CosmosReplaceAsync<T>(T entity, bool upsert, CancellationToken ct) where T : IDocumentEntity<T>
    {
        if (!T.Versioned)
        {
            if (upsert)
            {
                await Send(HttpMethod.Post, entity, null, upsert: true, ct).ConfigureAwait(false);
                return;
            }

            var replaced = await Send(HttpMethod.Put, entity, null, upsert: false, ct, HttpStatusCode.NotFound).ConfigureAwait(false);
            if (replaced.Status == HttpStatusCode.NotFound)
                throw new DocConcurrencyException($"{T.Collection}: no document with this key.");
            return;
        }

        var version = T.GetVersion(entity);
        var stored = await Read(entity, ct).ConfigureAwait(false);
        if (stored is null && !upsert)
            throw new DocConcurrencyException($"{T.Collection}: no document with this key at version {version} (deleted since it was read).");
        if (stored is { } current && current.Version != version)
            throw new DocConcurrencyException($"{T.Collection}: the document with this key is at version {current.Version}, not {version}.");
        T.SetVersion(entity, version + 1);
        try
        {
            var written = stored is { } found
                ? await Send(HttpMethod.Put, entity, found.ETag, upsert: false, ct, HttpStatusCode.PreconditionFailed, HttpStatusCode.NotFound).ConfigureAwait(false)
                : await Send(HttpMethod.Post, entity, null, upsert: false, ct, HttpStatusCode.Conflict).ConfigureAwait(false);
            if (written.Status is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound or HttpStatusCode.Conflict)
                throw new DocConcurrencyException($"{T.Collection}: the document with this key changed while it was written (version {version}).");
        }
        catch
        {
            T.SetVersion(entity, version); // the object keeps the version it had: the write did not happen
            throw;
        }
    }

    private async Task CosmosDeleteAsync<T>(T entity, CancellationToken ct) where T : IDocumentEntity<T>
    {
        string? etag = null;
        if (T.Versioned)
        {
            var stored = await Read(entity, ct).ConfigureAwait(false);
            if (stored is not { } current || current.Version != T.GetVersion(entity))
                throw new DocConcurrencyException($"{T.Collection}: no document with this key at version {T.GetVersion(entity)}.");
            etag = current.ETag;
        }

        var deleted = await Send(HttpMethod.Delete, entity, etag, upsert: false, ct, HttpStatusCode.NotFound, HttpStatusCode.PreconditionFailed).ConfigureAwait(false);
        if (T.Versioned && deleted.Status != HttpStatusCode.NoContent)
            throw new DocConcurrencyException($"{T.Collection}: the document with this key changed before it was deleted.");
    }

    /// <summary>
    ///     One document a command read (its etag kept by the read), written by its id with <c>If-Match</c> on that etag: deleted, or patched with
    ///     <paramref name="update" />'s operations (<c>application/json_patch+json</c>).
    /// </summary>
    internal async Task<DocCommandOutcome> WriteReadAsync<T>(T entity, DocPatchWriter<T>? update, object?[] slots, CancellationToken ct) where T : IDocumentEntity<T>
    {
        var etag = ETagOf(entity!) ?? throw new InvalidOperationException($"{T.Collection}: a document a command writes is read first, with its etag.");
        var link = $"dbs/{Database}/colls/{T.Collection}/docs/{Uri.EscapeDataString(T.Id(entity))}";
        byte[]? body = null;
        if (update is not null)
        {
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream))
            {
                w.WriteStartObject();
                w.WriteStartArray("operations");
                update(w, entity, slots);
                w.WriteEndArray();
                w.WriteEndObject();
            }

            body = stream.ToArray();
        }

        var partitionKey = PartitionKey(entity);
        var response = await CosmosClient.SendAsync(update is null ? HttpMethod.Delete : HttpMethod.Patch, "docs", link, link, body, h =>
        {
            h.TryAddWithoutValidation("x-ms-documentdb-partitionkey", partitionKey);
            h.TryAddWithoutValidation("If-Match", etag);
        }, "application/json_patch+json", ct, HttpStatusCode.PreconditionFailed, HttpStatusCode.NotFound).ConfigureAwait(false);
        return response.Status switch
        {
            HttpStatusCode.PreconditionFailed => DocCommandOutcome.Stale,
            HttpStatusCode.NotFound => DocCommandOutcome.Gone,
            _ => DocCommandOutcome.Written,
        };
    }

    /// <summary>The stored document's version and etag, or null when there is none.</summary>
    private async Task<(long Version, string ETag)?> Read<T>(T entity, CancellationToken ct) where T : IDocumentEntity<T>
    {
        var link = $"dbs/{Database}/colls/{T.Collection}/docs/{Uri.EscapeDataString(T.Id(entity))}";
        var response = await CosmosClient.SendAsync(HttpMethod.Get, "docs", link, link, null, h => h.TryAddWithoutValidation("x-ms-documentdb-partitionkey", PartitionKey(entity)), ct: ct, accepted: HttpStatusCode.NotFound).ConfigureAwait(false);
        if (response.Status == HttpStatusCode.NotFound)
            return null;
        using var document = JsonDocument.Parse(response.Body);
        var version = document.RootElement.TryGetProperty(DocVersion.Field, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
        return (version, document.RootElement.GetProperty("_etag").GetString()!);
    }

    /// <summary>One document's request; a create or replace that succeeds leaves its etag beside the object (<see cref="ETagOf" />).</summary>
    private async Task<CosmosResponse> Send<T>(HttpMethod method, T entity, string? ifMatch, bool upsert, CancellationToken ct, params HttpStatusCode[] accepted) where T : IDocumentEntity<T>
    {
        var parent = $"dbs/{Database}/colls/{T.Collection}";
        var link = method == HttpMethod.Post ? parent : $"{parent}/docs/{Uri.EscapeDataString(T.Id(entity))}";
        var path = method == HttpMethod.Post ? parent + "/docs" : link;
        byte[]? body = null;
        if (method != HttpMethod.Delete)
        {
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream))
                T.WriteJson(w, entity);
            body = stream.ToArray();
        }

        var partitionKey = PartitionKey(entity);
        var response = await CosmosClient.SendAsync(method, "docs", link, path, body, h =>
        {
            h.TryAddWithoutValidation("x-ms-documentdb-partitionkey", partitionKey);
            if (upsert)
                h.TryAddWithoutValidation("x-ms-documentdb-is-upsert", "True");
            if (ifMatch is not null)
                h.TryAddWithoutValidation("If-Match", ifMatch);
        }, ct: ct, accepted: accepted).ConfigureAwait(false);
        if (method != HttpMethod.Delete)
            Remember(entity, response);
        return response;
    }

    /// <summary>The <c>x-ms-documentdb-partitionkey</c> header: the value as a one-element JSON array.</summary>
    private static string PartitionKey<T>(T entity) where T : IDocumentEntity<T>
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartArray();
            T.WritePartitionKey(w, entity);
            w.WriteEndArray();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
