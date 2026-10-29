using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Events.Outbox;
using Pragmatic.Persistence.Documents;

namespace Pragmatic.Events.Engines.Documents;

/// <summary>
///     One delivery pass of the outbox of <typeparamref name="TBoundary" />, on the documents engine: the pending documents' keys read (oldest
///     first, a batch), claimed with one conditional update (pending, and unclaimed or past its lease: another worker's claim stands), the
///     documents that carry this pass's token loaded and delivered, each replaced. At least once: a crash between a dispatch and its replace
///     delivers the event again, so a handler must be idempotent.
/// </summary>
/// <remarks>
///     The documents are read through the boundary's own session (by its key). On Cosmos DB, which updates a document by its id only, the claim is
///     the engine's read-then-patch at each document's etag: a document another worker claimed in between is read again, and no longer selected.
/// </remarks>
public sealed class DocEventOutboxDrainer<TBoundary>(IServiceScopeFactory scopes, EventOutboxOptions options, ILogger<DocEventOutboxDrainer<TBoundary>> logger, TimeProvider? time = null)
    : IEngineEventOutboxDrainer<TBoundary>
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <inheritdoc />
    public async Task<int> DrainOnceAsync(CancellationToken ct = default)
    {
        var scope = scopes.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        var session = scope.ServiceProvider.GetRequiredKeyedService<DocSession>(typeof(TBoundary));
        var now = _time.GetUtcNow().UtcTicks;
        var token = EventOutboxPass.ClaimToken();

        // The keys first, then the claim by key: an update by filter writes every document it selects, so the batch is chosen before it.
        var candidates = await session.Query<EventOutboxDocument>()
            .Where((d, a) => d.ProcessedAt == null && d.Attempts < a.MaxAttempts && (d.ClaimedBy == null || d.ClaimedUntil == null || d.ClaimedUntil < a.Now),
                (options.MaxAttempts, Now: now))
            .OrderBy(d => d.CreatedAt).Take(options.BatchSize).Select(d => d.Id).ToListAsync(ct).ConfigureAwait(false);
        if (candidates.Count == 0)
            return 0;

        var claimed = await session.Query<EventOutboxDocument>()
            .Where((d, a) => a.Ids.Contains(d.Id) && d.ProcessedAt == null && (d.ClaimedBy == null || d.ClaimedUntil == null || d.ClaimedUntil < a.Now),
                (Ids: candidates, Now: now))
            .ExecuteUpdateAsync((s, a) => s.Set(d => d.ClaimedBy, a.Token).Set(d => d.ClaimedUntil, a.Until),
                (Token: token, Until: (long?)(now + options.ClaimLeaseDuration.Ticks)), ct).ConfigureAwait(false);
        if (claimed == 0)
            return 0;

        var documents = await session.Query<EventOutboxDocument>().Where((d, t) => d.ClaimedBy == t && d.ProcessedAt == null, token)
            .OrderBy(d => d.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        var delivered = await EventOutboxPass.DeliverAsync(documents, scope.ServiceProvider, _time, logger, ct).ConfigureAwait(false);
        foreach (var document in documents)
            await session.ReplaceAsync(document, ct).ConfigureAwait(false);
        return delivered;
    }
}
