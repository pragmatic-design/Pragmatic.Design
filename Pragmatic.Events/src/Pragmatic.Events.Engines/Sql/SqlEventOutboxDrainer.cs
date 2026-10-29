using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Events.Outbox;
using Pragmatic.Persistence.Sql;

namespace Pragmatic.Events.Engines.Sql;

/// <summary>
///     One delivery pass of the outbox of <typeparamref name="TBoundary" />, on the Sql engine: the pending rows' keys read (oldest first, a
///     batch), claimed with one conditional update (pending, and unclaimed or past its lease: another worker's claim stands), the rows that
///     carry this pass's token loaded and delivered, each written back. At least once: a crash between a dispatch and its write back delivers
///     the event again, so a handler must be idempotent.
/// </summary>
/// <remarks>The rows are read through the boundary's own session (by its key), the one its unit of work writes them through.</remarks>
public sealed class SqlEventOutboxDrainer<TBoundary>(IServiceScopeFactory scopes, EventOutboxOptions options, ILogger<SqlEventOutboxDrainer<TBoundary>> logger, TimeProvider? time = null)
    : IEngineEventOutboxDrainer<TBoundary>
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <inheritdoc />
    public async Task<int> DrainOnceAsync(CancellationToken ct = default)
    {
        var scope = scopes.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        var session = scope.ServiceProvider.GetRequiredKeyedService<SqlSession>(typeof(TBoundary));
        var now = _time.GetUtcNow().UtcTicks;
        var token = EventOutboxPass.ClaimToken();

        // The keys first, then the claim by key: a batch's Take is no part of an UPDATE on every database.
        var candidates = await session.Query<EventOutboxRow>()
            .Where((r, a) => r.ProcessedAt == null && r.Attempts < a.MaxAttempts && (r.ClaimedBy == null || r.ClaimedUntil == null || r.ClaimedUntil < a.Now),
                (options.MaxAttempts, Now: now))
            .OrderBy(r => r.CreatedAt).Take(options.BatchSize).Select(r => r.Id).ToListAsync(ct).ConfigureAwait(false);
        if (candidates.Count == 0)
            return 0;

        var claimed = await session.Query<EventOutboxRow>()
            .Where((r, a) => a.Ids.Contains(r.Id) && r.ProcessedAt == null && (r.ClaimedBy == null || r.ClaimedUntil == null || r.ClaimedUntil < a.Now),
                (Ids: candidates, Now: now))
            .ExecuteUpdateAsync((s, a) => s.SetProperty(r => r.ClaimedBy, a.Token).SetProperty(r => r.ClaimedUntil, a.Until),
                (Token: token, Until: (long?)(now + options.ClaimLeaseDuration.Ticks)), ct).ConfigureAwait(false);
        if (claimed == 0)
            return 0;

        var rows = await session.Query<EventOutboxRow>().Where((r, t) => r.ClaimedBy == t && r.ProcessedAt == null, token)
            .OrderBy(r => r.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        var delivered = await EventOutboxPass.DeliverAsync(rows, scope.ServiceProvider, _time, logger, ct).ConfigureAwait(false);
        foreach (var row in rows)
            await session.UpdateAsync(row, ct).ConfigureAwait(false);
        return delivered;
    }
}
