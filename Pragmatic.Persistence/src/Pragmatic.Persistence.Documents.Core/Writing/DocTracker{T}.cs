using System.Collections.Concurrent;

namespace Pragmatic.Persistence.Documents.Writing;

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
