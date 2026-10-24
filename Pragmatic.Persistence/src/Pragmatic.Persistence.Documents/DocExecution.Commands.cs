namespace Pragmatic.Persistence.Documents;

/// <summary>
///     The commands of a translated chain on MongoDB: a <c>delete</c> or an <c>update</c> (<c>multi</c>) of every document the filter selects,
///     in one round trip; the count from the reply's <c>n</c>. Cosmos DB has no delete or update by filter (a document is written by its id):
///     refused, with the remedy.
/// </summary>
public static partial class DocExecution
{
    private const string CosmosCommands = "Cosmos DB writes a document by its id, not by a filter: read the documents (or their ids) with the chain and write each one";

    public static async ValueTask<long> ExecuteDeleteAsync<TEntity, TResult>(DocQuery<TResult> query, DocPlan<TEntity> plan, CancellationToken ct) where TEntity : IDocumentEntity<TEntity>
    {
        using var prepared = DocFamilies.Use(await DocFamilies.PrepareAsync<TEntity>(query.Session, ct).ConfigureAwait(false)); // a trait's child: its visible parents, read first
        var session = Commanded(query, plan);
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
        var slots = query.SlotArray;
        var reply = await session.RunAsync(w =>
        {
            w.Write("update", TEntity.Collection);
            w.StartArray("updates");
            w.StartDocument(0);
            plan.WriteFilter(w, "q", slots, session);
            w.StartDocument("u");
            plan.Update!(w, slots);
            w.EndDocument();
            w.Write("multi", true);
            w.EndDocument();
            w.EndArray();
        }, ct).ConfigureAwait(false);
        return Touched(reply);
    }

    private static DocSession Commanded<TEntity, TResult>(DocQuery<TResult> query, DocPlan<TEntity> plan) where TEntity : IDocumentEntity<TEntity>
    {
        if (query.Session.Cosmos is not null)
            throw new NotSupportedException($"{plan.Origin}: {CosmosCommands}.");
        if (query.SkipCount >= 0 || query.TakeCount >= 0)
            throw new NotSupportedException($"{plan.Origin}: a delete or an update by filter writes every document the filter selects: no Skip or Take before it.");
        return query.Session;
    }

    private static long Touched(Mongo.Bson.BsonDocument reply)
        => reply.TryGetInt64("n", out var n) ? n : reply.TryGetNumber("n", out var number) ? (long)number : throw new Mongo.MongoException("The command's reply holds no n.");
}
