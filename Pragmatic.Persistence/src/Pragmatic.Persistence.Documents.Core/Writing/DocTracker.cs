using System.Collections.Concurrent;

namespace Pragmatic.Persistence.Documents.Writing;

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
