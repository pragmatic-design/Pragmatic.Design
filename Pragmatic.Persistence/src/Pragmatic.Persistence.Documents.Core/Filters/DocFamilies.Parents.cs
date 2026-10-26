using System.Text.Json;
using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Filters;

/// <summary>
///     A trait's child visible only where its parent is: Pragmatic's <c>ParentVisibilityFilter</c> (the parent's owner and scopes) and its parent's
///     tenancy (EF Core's named <c>"ParentTenant"</c>). A document store's filter cannot read another collection, so before an execution reads such
///     a child the keys of the parents each active family admits are read (one query on the parents), kept for the execution (an async-local
///     value, from the terminal down), and the child's family matches its foreign key against them. One more query per family per read of the
///     child; none when the family is lifted.
/// </summary>
public static partial class DocFamilies
{
    private static readonly AsyncLocal<Dictionary<(Type Child, DocFilterFamily Family), IReadOnlyList<Guid>>?> Prepared = new();

    /// <summary>
    ///     Reads the parents every active parent family of <typeparamref name="TEntity" /> admits for the execution about to run; null when it has
    ///     none. <see cref="Use" /> makes them the execution's: an async method's async-local value does not flow back to its caller.
    /// </summary>
    public static async ValueTask<Dictionary<(Type Child, DocFilterFamily Family), IReadOnlyList<Guid>>?> PrepareAsync<TEntity>(DocSession session, CancellationToken ct)
        where TEntity : IDocumentEntity<TEntity>
    {
        Dictionary<(Type, DocFilterFamily), IReadOnlyList<Guid>>? prepared = null;
        foreach (var family in TEntity.Families)
        {
            if (family.VisibleParents is not { } read || (session.Filters?.StateOf(family.Slot) ?? DocFilterState.Active) != DocFilterState.Active)
                continue;
            prepared ??= [];
            prepared[(typeof(TEntity), family.Slot.Family)] = await read(session, session.Filters, ct).ConfigureAwait(false);
        }

        return prepared;
    }

    /// <summary>The prepared parents for the rest of the calling method (and what it awaits); disposing restores what was there (a join's outer side).</summary>
    public static IDisposable? Use(Dictionary<(Type Child, DocFilterFamily Family), IReadOnlyList<Guid>>? prepared)
    {
        if (prepared is null)
            return null;
        var previous = Prepared.Value;
        Prepared.Value = previous is null
            ? prepared
            : new Dictionary<(Type, DocFilterFamily), IReadOnlyList<Guid>>(previous.Concat(prepared).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value));
        return new Restore(previous);
    }

    /// <summary>The parents <typeparamref name="TChild" />'s <paramref name="family" /> matches, as prepared for this execution.</summary>
    public static IReadOnlyList<Guid> VisibleParents<TChild>(DocFilterFamily family)
        => Prepared.Value is { } prepared && prepared.TryGetValue((typeof(TChild), family), out var ids)
            ? ids
            : throw new InvalidOperationException($"The visible parents of {typeof(TChild).Name} were not read before this execution (DocFamilies.PrepareAsync): its documents are not read without them.");

    /// <summary>
    ///     The keys of the <typeparamref name="TParent" /> documents of the caller's tenant (<paramref name="tenantField" />): none when the tenant
    ///     is not resolved, as the tenant's own family reads nothing then.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> TenantParentKeysAsync<TParent>(DocSession session, IDocFilters? filters, string tenantField, CancellationToken ct)
        where TParent : IDocumentEntity<TParent>
    {
        if (filters?.TenantId is not { } tenant)
            return [];
        var keys = new List<Guid>();
        if (session.Cosmos is { } cosmos)
        {
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream))
            {
                w.WriteStartObject();
                w.WriteString("query", $"SELECT VALUE c.id FROM c WHERE c[\"{tenantField}\"] = @__tenant");
                w.WriteStartArray("parameters");
                w.WriteStartObject(); w.WriteString("name", "@__tenant"); w.WriteString("value", tenant); w.WriteEndObject();
                w.WriteEndArray();
                w.WriteEndObject();
            }

            await foreach (var id in cosmos.QueryAsync(session.Database, TParent.Collection, stream.ToArray(), null, null, ct).ConfigureAwait(false))
                keys.Add(Guid.Parse(id.GetString()!));
            return keys;
        }

        var reply = await session.RunAsync(w =>
        {
            w.Write("find", TParent.Collection);
            w.StartDocument("filter");
            w.Write(tenantField, tenant);
            w.EndDocument();
            w.StartDocument("projection");
            w.Write("_id", 1);
            w.EndDocument();
        }, ct).ConfigureAwait(false);
        return await KeysAsync(session, reply, keys, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     The keys of the <typeparamref name="TParent" /> documents the caller may see by the parent's own owner (<paramref name="ownerField" />) and
    ///     scopes (<paramref name="scopesField" />): none for an anonymous caller.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> ParentKeysAsync<TParent>(DocSession session, IDocFilters? filters, string? ownerField, string? scopesField, CancellationToken ct)
        where TParent : IDocumentEntity<TParent>
    {
        var user = filters?.UserId;
        var scopes = filters?.Scopes ?? [];
        if (user is null)
            return [];
        var keys = new List<Guid>();
        if (session.Cosmos is { } cosmos)
        {
            var conditions = new List<string>();
            if (ownerField is not null)
                conditions.Add($"c[\"{ownerField}\"] = @__user");
            if (scopesField is not null)
                conditions.Add($"EXISTS(SELECT VALUE s FROM s IN c[\"{scopesField}\"] WHERE ARRAY_CONTAINS(@__scopes, s))");
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream))
            {
                w.WriteStartObject();
                w.WriteString("query", "SELECT VALUE c.id FROM c WHERE " + string.Join(" OR ", conditions));
                w.WriteStartArray("parameters");
                w.WriteStartObject(); w.WriteString("name", "@__user"); w.WriteString("value", user); w.WriteEndObject();
                w.WriteStartObject(); w.WriteString("name", "@__scopes"); w.WriteStartArray("value");
                foreach (var scope in scopes)
                    w.WriteStringValue(scope);
                w.WriteEndArray(); w.WriteEndObject();
                w.WriteEndArray();
                w.WriteEndObject();
            }

            await foreach (var id in cosmos.QueryAsync(session.Database, TParent.Collection, stream.ToArray(), null, null, ct).ConfigureAwait(false))
                keys.Add(Guid.Parse(id.GetString()!));
            return keys;
        }

        var reply = await session.RunAsync(w =>
        {
            w.Write("find", TParent.Collection);
            w.StartDocument("filter");
            w.StartArray("$or");
            var i = 0;
            if (ownerField is not null)
            {
                w.StartDocument(i++);
                w.Write(ownerField, user);
                w.EndDocument();
            }

            if (scopesField is not null)
            {
                w.StartDocument(i);
                w.StartDocument(scopesField);
                DocValues.WriteArray(w, "$in", scopes, DocValues.Write);
                w.EndDocument();
                w.EndDocument();
            }

            w.EndArray();
            w.EndDocument();
            w.StartDocument("projection");
            w.Write("_id", 1);
            w.EndDocument();
        }, ct).ConfigureAwait(false);
        return await KeysAsync(session, reply, keys, ct).ConfigureAwait(false);
    }

    /// <summary>The <c>_id</c> of every document of a find's reply, its later batches included.</summary>
    private static async Task<IReadOnlyList<Guid>> KeysAsync(DocSession session, BsonDocument reply, List<Guid> keys, CancellationToken ct)
    {
        var cursor = session.Cursor(reply);
        await using (((IAsyncDisposable)cursor).ConfigureAwait(false)) // a cursor is enumerable too: the disposal's ConfigureAwait, named
        {
            await foreach (var document in cursor.WithCancellation(ct).ConfigureAwait(false))
            {
                var reader = new BsonReader(document.Bytes.Span);
                while (reader.ReadElement(out _, out var name))
                {
                    if (name.SequenceEqual("_id"u8))
                        keys.Add(reader.ReadGuid());
                    else
                        reader.Skip();
                }
            }
        }

        return keys;
    }

    private sealed class Restore(Dictionary<(Type Child, DocFilterFamily Family), IReadOnlyList<Guid>>? previous) : IDisposable
    {
        public void Dispose() => Prepared.Value = previous;
    }
}
