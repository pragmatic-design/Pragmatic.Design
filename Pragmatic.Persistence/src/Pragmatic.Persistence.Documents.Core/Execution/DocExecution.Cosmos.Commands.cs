namespace Pragmatic.Persistence.Documents.Execution;

/// <summary>
///     A delete or an update by filter on Cosmos DB, which writes a document by its id only: the documents the chain selects are read (its query,
///     the caller's families), then each one is deleted, or patched with the update's operations, with <c>If-Match</c> on the etag of that read. A
///     document another writer changed in between (412) is read again and, if the chain still selects it, written again: three rounds, then
///     <see cref="DocConcurrencyException" />. Not atomic: Cosmos DB has no transaction across documents, so the documents written before a failure
///     stay written. The count is the documents written.
/// </summary>
public static partial class DocExecution
{
    private const int CommandRounds = 3;

    private static async ValueTask<long> CosmosCommandAsync<TEntity, TResult>(DocQuery<TResult> query, DocPlan<TEntity> plan, bool delete, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        if (!delete && plan.CosmosUpdate is null)
            throw new NotSupportedException($"{plan.Origin}: this update has no Cosmos DB form: a patch takes ten operations at most (see the generator's note at the call site).");
        var session = query.Session;
        var documents = query.As<TEntity>(); // the same slots, read whole: a computed value is computed from the document
        var slots = query.SlotArray;
        HashSet<string>? pending = null;
        long written = 0;
        for (var round = 1; ; round++)
        {
            var stale = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entity in await CosmosListAsync(documents, plan, ct).ConfigureAwait(false))
            {
                var id = TEntity.Id(entity);
                if (pending is not null && !pending.Contains(id))
                    continue; // written in an earlier round
                switch (await session.WriteReadAsync(entity, delete ? null : plan.CosmosUpdate, slots, ct).ConfigureAwait(false))
                {
                    case DocCommandOutcome.Written:
                        written++;
                        break;
                    case DocCommandOutcome.Stale:
                        stale.Add(id);
                        break;
                }
            }

            if (stale.Count == 0)
                return written;
            if (round == CommandRounds)
                throw new DocConcurrencyException(TEntity.Collection, $"{plan.Origin}: {stale.Count} document(s) changed while the command wrote them, {CommandRounds} rounds in a row ({written} written).");
            pending = stale;
        }
    }
}
