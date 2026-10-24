using System.Collections.Concurrent;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     A tracked document type's operations without its type argument: what a unit of work, which holds entities as objects (<c>Add(object)</c>),
///     calls. One per type, registered at module start by the assembly that declares the type (its generated change detection), as the Sql
///     engine's maps are.
/// </summary>
public abstract class DocTracker
{
    public abstract Type Type { get; }

    public abstract string Collection { get; }

    public abstract object?[] Snapshot(object entity);

    public abstract ulong Changed(object entity, object?[] snapshot);

    public abstract Task InsertAsync(DocSession session, object entity, CancellationToken ct);

    /// <summary>MongoDB: the fields of <paramref name="mask" /> set; Cosmos DB: the document replaced at the etag it was read with.</summary>
    public abstract Task UpdateAsync(DocSession session, object entity, ulong mask, CancellationToken ct);

    public abstract Task DeleteAsync(DocSession session, object entity, CancellationToken ct);
}

/// <summary>The tracker of <typeparamref name="T" />.</summary>
public sealed class DocTracker<T> : DocTracker where T : class, IDocumentTracked<T>
{
    public override Type Type => typeof(T);

    public override string Collection => T.Collection;

    public override object?[] Snapshot(object entity) => T.Snapshot((T)entity);

    public override ulong Changed(object entity, object?[] snapshot) => T.Changed((T)entity, snapshot);

    public override Task InsertAsync(DocSession session, object entity, CancellationToken ct) => session.InsertAsync((T)entity, ct);

    public override Task UpdateAsync(DocSession session, object entity, ulong mask, CancellationToken ct)
        => session.Cosmos is not null ? session.ReplaceTrackedAsync((T)entity, ct) : session.UpdateFieldsAsync((T)entity, mask, ct);

    public override Task DeleteAsync(DocSession session, object entity, CancellationToken ct) => session.DeleteTrackedAsync((T)entity, ct);
}

/// <summary>The trackers registered, by type.</summary>
public static class DocTrackers
{
    private static readonly ConcurrentDictionary<Type, DocTracker> ByType = new();

    /// <summary>Called by the generated change detection at module start.</summary>
    public static void Register<T>() where T : class, IDocumentTracked<T> => ByType.TryAdd(typeof(T), new DocTracker<T>());

    /// <summary>The tracker of <paramref name="type" />; refused when the type is not a document a unit of work tracks.</summary>
    public static DocTracker For(Type type)
        => ByType.TryGetValue(type, out var tracker) ? tracker
            : throw new ArgumentException($"{type.Name} is not tracked as a document: a Pragmatic entity of an assembly the documents generator ran on is (its change detection registers it).", nameof(type));
}
