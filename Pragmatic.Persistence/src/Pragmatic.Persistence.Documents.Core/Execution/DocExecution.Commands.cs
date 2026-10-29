namespace Pragmatic.Persistence.Documents.Execution;

/// <summary>
///     The commands of a translated chain on MongoDB: a <c>delete</c> or an <c>update</c> (<c>multi</c>) of every document the filter selects,
///     in one round trip; the count from the reply's <c>n</c>. Cosmos DB has no delete or update by filter (a document is written by its id): there
///     the documents the chain selects are read, then written one by one (<see cref="CosmosCommandAsync{TEntity, TResult}" />).
/// </summary>
public static partial class DocExecution
{
    public static async ValueTask<long> ExecuteDeleteAsync<TEntity, TResult>(DocQuery<TResult> query, DocPlan<TEntity> plan, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        using var prepared = DocFamilies.Use(await DocFamilies.PrepareAsync<TEntity>(query.Session, ct).ConfigureAwait(false)); // a trait's child: its visible parents, read first
        var session = Commanded(query, plan);
        if (session.Cosmos is not null)
            return await CosmosCommandAsync(query, plan, delete: true, ct).ConfigureAwait(false);
        var slots = query.SlotArray;
        var reply = await session.RunAsync(w =>
        {
            w.Write("delete", TEntity.Collection);
            w.StartArray("deletes");
            w.StartDocument(0);
            plan.WriteFilter(w, "q", slots, session);
            w.Write("limit", 0);
            w.EndDocument();
            w.EndArray();
        }, ct).ConfigureAwait(false);
        return Touched(reply);
    }

    public static async ValueTask<long> ExecuteUpdateAsync<TEntity, TResult>(DocQuery<TResult> query, DocPlan<TEntity> plan, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        using var prepared = DocFamilies.Use(await DocFamilies.PrepareAsync<TEntity>(query.Session, ct).ConfigureAwait(false)); // a trait's child: its visible parents, read first
        var session = Commanded(query, plan);
        if (session.Cosmos is not null)
            return await CosmosCommandAsync(query, plan, delete: false, ct).ConfigureAwait(false);
        var slots = query.SlotArray;
        var reply = await session.RunAsync(w =>
        {
            w.Write("update", TEntity.Collection);
            w.StartArray("updates");
            w.StartDocument(0);
            plan.WriteFilter(w, "q", slots, session);
            if (plan.UpdatePipeline)
            {
                w.StartArray("u"); // an update pipeline: its $set stage reads the document's own fields
                w.StartDocument(0);
                plan.Update!(w, slots);
                w.EndDocument();
                w.EndArray();
            }
            else
            {
                w.StartDocument("u");
                plan.Update!(w, slots);
                w.EndDocument();
            }

            w.Write("multi", true);
            w.EndDocument();
            w.EndArray();
        }, ct).ConfigureAwait(false);
        return Touched(reply);
    }

    private static DocSession Commanded<TEntity, TResult>(DocQuery<TResult> query, DocPlan<TEntity> plan) where TEntity : IDocumentEntity<TEntity>
    {
        if (query.SkipCount >= 0 || query.TakeCount >= 0)
            throw new NotSupportedException($"{plan.Origin}: a delete or an update by filter writes every document the filter selects: no Skip or Take before it.");
        return query.Session;
    }

    private static long Touched(Mongo.Bson.BsonDocument reply)
        => reply.TryGetInt64("n", out var n) ? n : reply.TryGetNumber("n", out var number) ? (long)number : throw new Mongo.MongoException("The command's reply holds no n.");
}
