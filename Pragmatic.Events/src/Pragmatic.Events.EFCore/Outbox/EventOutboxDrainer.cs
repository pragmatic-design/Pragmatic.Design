using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Events.Outbox;
using Pragmatic.Serialization;

namespace Pragmatic.Events.EFCore.Outbox;

/// <summary>
///     One outbox delivery pass: claim eligible rows, dispatch, mark processed.
/// </summary>
/// <remarks>
///     <para>
///         The whole of the delivery logic, and the only copy of it. It is a public service rather
///         than an <c>internal</c> method of <see cref="EventOutboxDeliveryService{TContext}" />
///         because a method reachable only from this assembly's tests leaves a consumer application
///         able to assert nothing about its own outbox except by waiting five seconds for a
///         background loop.
///     </para>
///     <para>
///         Delivery is <b>at-least-once</b>: an event may be dispatched more than once if the process
///         crashes between the dispatch and the mark-processed write. Handlers must be idempotent.
///         Across replicas rows are claimed atomically (<c>ClaimedBy</c>/<c>ClaimedUntil</c>) so
///         concurrent workers do not each deliver the same entry, and an expired claim — a crashed
///         worker — is re-grabbed after the lease window.
///     </para>
/// </remarks>
/// <typeparam name="TContext">The DbContext that owns the outbox table.</typeparam>
public sealed class EventOutboxDrainer<TContext> : IEventOutboxDrainer<TContext>
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EventOutboxOptions _options;
    private readonly ILogger<EventOutboxDrainer<TContext>> _logger;

    /// <summary>Creates the drainer.</summary>
    public EventOutboxDrainer(
        IServiceScopeFactory scopeFactory,
        EventOutboxOptions options,
        ILogger<EventOutboxDrainer<TContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> DrainOnceAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        // Closed allowlist of event types (replaces unsafe Type.GetType on a DB string).
        var typeResolver = scope.ServiceProvider.GetRequiredService<IEventOutboxTypeResolver>();

        // SG-generated JsonSerializerContext (AOT-safe) — the JsonTypeInfo overload avoids reflection.
        var serializerOptions = (scope.ServiceProvider.GetService<PragmaticJsonOptions>() ?? PragmaticJsonOptions.Default).Build();

        var now = DateTimeOffset.UtcNow;
        var claimedUntil = now + _options.ClaimLeaseDuration;

        // Unique per-poll token: atomically stamp eligible rows, then process only what we won.
        // Without this every replica selects the same rows and dispatches each event N times.
        var claimToken = $"{Environment.MachineName}:{Guid.NewGuid():N}";

        // A row is eligible when: pending, attempts remaining, and either unclaimed or its claim
        // has expired (so a crashed worker's entries are re-grabbable rather than stuck).
        // Select candidate ids first (Take is not translatable inside ExecuteUpdate on every
        // provider, e.g. SQLite), then claim them with a CAS guard.
        var candidateIds = await context.Set<EventOutboxEntry>()
            .Where(e => e.ProcessedAt == null
                && e.Attempts < _options.MaxAttempts
                && (e.ClaimedBy == null || e.ClaimedUntil == null || e.ClaimedUntil < now))
            .OrderBy(e => e.CreatedAt)
            .Take(_options.BatchSize)
            .Select(e => e.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        if (candidateIds.Count == 0)
            return 0;

        var claimed = await context.Set<EventOutboxEntry>()
            .Where(e => candidateIds.Contains(e.Id)
                && e.ProcessedAt == null
                && (e.ClaimedBy == null || e.ClaimedUntil == null || e.ClaimedUntil < now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.ClaimedBy, claimToken)
                .SetProperty(e => e.ClaimedUntil, claimedUntil), ct)
            .ConfigureAwait(false);

        if (claimed == 0)
            return 0;

        // Load (tracked) only the rows this worker actually claimed.
        var pending = await context.Set<EventOutboxEntry>()
            .Where(e => e.ClaimedBy == claimToken && e.ProcessedAt == null)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);

        if (pending.Count == 0)
            return 0;

        var delivered = 0;

        foreach (var entry in pending)
        {
            try
            {
                var refused = await EventOutboxDelivery.DeliverAsync(entry.EventType, entry.Payload, entry.TenantId, entry.TraceParent,
                    typeResolver, serializerOptions, dispatcher, ct).ConfigureAwait(false);
                if (refused is not null)
                {
                    entry.Attempts++;
                    entry.LastError = refused;
                    entry.ClaimedBy = null;
                    entry.ClaimedUntil = null;
                    _logger.LogWarning("Skipped outbox event {OutboxEntryId} of type {EventType}: {Reason}", entry.Id, entry.EventType, refused);
                    continue;
                }

                entry.ProcessedAt = DateTimeOffset.UtcNow;
                delivered++;
            }
            catch (Exception ex)
            {
                entry.Attempts++;
                // Use ex.ToString() for full type + message + inner chain; truncate to 2000 chars
                // to avoid exceeding typical varchar column limits on narrow DB configurations.
                var errorText = ex.ToString();
                entry.LastError = errorText.Length <= 2000 ? errorText : errorText[..2000];
                // Release the claim so the entry is re-grabbable on a later poll (within MaxAttempts).
                entry.ClaimedBy = null;
                entry.ClaimedUntil = null;
                _logger.LogError(ex, "Failed to deliver outbox event {OutboxEntryId}", entry.Id);
            }
        }

        await context.SaveChangesAsync(ct).ConfigureAwait(false);

        return delivered;
    }
}
