using System.Text.Json;
using Pragmatic.Persistence.Documents.Mongo.Bson;

namespace Pragmatic.Persistence.Documents.Filters;

/// <summary>
///     A trait's child visible only where its parent is (Pragmatic's <c>ParentVisibilityFilter</c>). A document store's filter cannot read another
///     collection, so before an execution reads such a child the keys of the parents the caller may see are read (one query on the parents' own
///     owner and scopes), kept for the execution (an async-local value, from the terminal down), and the child's family matches its foreign key
///     against them. One more query per read of the child; none when the family is lifted.
/// </summary>
public static partial class DocFamilies
{
    private static readonly AsyncLocal<Dictionary<Type, IReadOnlyList<Guid>>?> Prepared = new();

    /// <summary>
    ///     Reads the visible parents of every active parent family of <typeparamref name="TEntity" /> for the execution about to run; null when it
    ///     has none. <see cref="Use" /> makes them the execution's: an async method's async-local value does not flow back to its caller.
    /// </summary>
    public static async ValueTask<Dictionary<Type, IReadOnlyList<Guid>>?> PrepareAsync<TEntity>(DocSession session, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        Dictionary<Type, IReadOnlyList<Guid>>? prepared = null;
        foreach (var family in TEntity.Families)
        {
            if (family.VisibleParents is not { } read || (session.Filters?.StateOf(family.Slot) ?? DocFilterState.Active) != DocFilterState.Active)
                continue;
            prepared ??= [];
            prepared[typeof(TEntity)] = await read(session, session.Filters, ct).ConfigureAwait(false);
        }

        return prepared;
    }

    /// <summary>The prepared parents for the rest of the calling method (and what it awaits); disposing restores what was there (a join's outer side).</summary>
    public static IDisposable? Use(Dictionary<Type, IReadOnlyList<Guid>>? prepared)
    {
        if (prepared is null)
            return null;
        var previous = Prepared.Value;
        Prepared.Value = previous is null ? prepared : new Dictionary<Type, IReadOnlyList<Guid>>(previous.Concat(prepared).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value));
        return new Restore(previous);
    }

    /// <summary>The parents <typeparamref name="TChild" />'s family matches, as prepared for this execution.</summary>
    public static IReadOnlyList<Guid> VisibleParents<TChild>()
        => Prepared.Value is { } prepared && prepared.TryGetValue(typeof(TChild), out var ids)
            ? ids
            : throw new InvalidOperationException($"The visible parents of {typeof(TChild).Name} were not read before this execution (DocFamilies.PrepareAsync): its documents are not read without them.");

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

    private sealed class Restore(Dictionary<Type, IReadOnlyList<Guid>>? previous) : IDisposable
    {
        public void Dispose() => Prepared.Value = previous;
    }
}
