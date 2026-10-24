namespace Pragmatic.Persistence.Documents.Execution;

/// <summary>
///     A grouped chain on MongoDB (<c>GroupBy(key).Select(g => new { g.Key, … })</c>): an <c>aggregate</c> of the filter's <c>$match</c> and the
///     plan's <c>$group</c>, then the HAVING's <c>$match</c>, the groups' <c>$sort</c> and Skip/Take as <c>$skip</c>/<c>$limit</c> (only with an
///     ordering: unordered groups come in the server's order), each result document built into the Select's object by the generated reader.
///     Cosmos DB's GROUP BY is not translated by this version: refused.
/// </summary>
public static partial class DocExecution
{
    public static async ValueTask<List<TResult>> GroupListAsync<TEntity, TResult>(DocQuery<TResult> query, DocPlan<TEntity> plan, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        using var prepared = DocFamilies.Use(await DocFamilies.PrepareAsync<TEntity>(query.Session, ct).ConfigureAwait(false)); // a trait's child: its visible parents, read first
        if (query.Session.Cosmos is not null)
            throw new NotSupportedException($"{plan.Origin}: a GroupBy is translated for MongoDB only; on Cosmos DB read the documents (in their partition) and group them in .NET.");
        if (query.TakeCount == 0)
            return []; // MongoDB's $limit takes a positive number
        if ((query.SkipCount >= 0 || query.TakeCount >= 0) && plan.GroupSort is null)
            throw new NotSupportedException($"{plan.Origin}: Skip or Take over groups needs their ordering (OrderBy over the Select's members): unordered groups come in the server's order.");
        var session = query.Session;
        var slots = query.SlotArray;
        var reply = await session.RunAsync(w =>
        {
            w.Write("aggregate", TEntity.Collection);
            w.StartArray("pipeline");
            w.StartDocument(0);
            plan.WriteFilter(w, "$match", slots, session);
            w.EndDocument();
            w.StartDocument(1);
            w.StartDocument("$group");
            plan.Group!(w);
            w.EndDocument();
            w.EndDocument();
            var stage = 2;
            if (plan.Having is not null)
            {
                w.StartDocument(stage++);
                w.StartDocument("$match");
                plan.Having(w, slots);
                w.EndDocument();
                w.EndDocument();
            }

            if (plan.GroupSort is not null)
            {
                w.StartDocument(stage++);
                w.StartDocument("$sort");
                plan.GroupSort(w);
                w.EndDocument();
                w.EndDocument();
            }

            if (query.SkipCount > 0)
            {
                w.StartDocument(stage++);
                w.Write("$skip", (long)query.SkipCount);
                w.EndDocument();
            }

            if (query.TakeCount >= 0)
            {
                w.StartDocument(stage);
                w.Write("$limit", (long)query.TakeCount);
                w.EndDocument();
            }

            w.EndArray();
            w.StartDocument("cursor");
            w.EndDocument();
        }, ct).ConfigureAwait(false);
        var documents = await session.Cursor(reply).ToListAsync(ct).ConfigureAwait(false);
        var results = new List<TResult>(documents.Count);
        foreach (var document in documents)
            results.Add((TResult)plan.GroupRead!(document));
        return results;
    }

    /// <summary>
    ///     A count inside a transaction, where the server refuses the <c>count</c> command (OperationNotSupportedInTransaction): an
    ///     <c>aggregate</c> of the <c>$match</c>, the paging and <c>$count</c>, whose one document holds the number (none: zero).
    /// </summary>
    private static async ValueTask<long> CountInTransactionAsync<TEntity, TResult>(DocQuery<TResult> query, DocPlan<TEntity> plan, object?[] slots, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        var session = query.Session;
        var reply = await session.RunAsync(w =>
        {
            w.Write("aggregate", TEntity.Collection);
            w.StartArray("pipeline");
            var stage = 0;
            w.StartDocument(stage++);
            plan.WriteFilter(w, "$match", slots, session);
            w.EndDocument();
            if (query.SkipCount >= 0)
            {
                w.StartDocument(stage++);
                w.Write("$skip", (long)query.SkipCount);
                w.EndDocument();
            }

            if (query.TakeCount >= 0)
            {
                w.StartDocument(stage++);
                w.Write("$limit", (long)query.TakeCount);
                w.EndDocument();
            }

            w.StartDocument(stage);
            w.Write("$count", "n");
            w.EndDocument();
            w.EndArray();
            w.StartDocument("cursor");
            w.EndDocument();
        }, ct).ConfigureAwait(false);
        var rows = await session.Cursor(reply).ToListAsync(ct).ConfigureAwait(false);
        return rows.Count == 0 ? 0 : DocValues.ReadScalar<long>(rows[0], "n");
    }
}
