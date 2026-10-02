using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.MultiTenancy;
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
    /// <summary>Trace source for outbox delivery spans; links back to the originating request trace.</summary>
    /// <remarks>
    ///     The source's <em>name</em> is what observability configuration keys on, and it is unchanged
    ///     — only the type that declares it moved here, beside the code that starts the span.
    /// </remarks>
    public static readonly ActivitySource ActivitySource = new("Pragmatic.Events.Outbox");

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
                // Fail-closed: only allowlisted (registered-handler) event types resolve.
                var eventType = typeResolver.Resolve(entry.EventType);
                if (eventType is null)
                {
                    entry.Attempts++;
                    // The allowlist is built from registered IDomainEventHandler<T> types, so a miss means
                    // no handler is registered for this event (or the type was renamed/moved since it was written).
                    entry.LastError =
                        $"No registered handler for event type '{entry.EventType}' — the outbox deserialization " +
                        "allowlist is derived from IDomainEventHandler<T> registrations. Register a handler, or the type was renamed.";
                    entry.ClaimedBy = null;
                    entry.ClaimedUntil = null;
                    _logger.LogWarning("Skipped outbox event {OutboxEntryId}: no registered handler for type {EventType}", entry.Id, entry.EventType);
                    continue;
                }

                if (JsonSerializer.Deserialize(entry.Payload, serializerOptions.GetTypeInfo(eventType)) is not IDomainEvent domainEvent)
                {
                    entry.Attempts++;
                    entry.LastError = "Payload did not deserialize to an IDomainEvent.";
                    entry.ClaimedBy = null;
                    entry.ClaimedUntil = null;
                    continue;
                }

                // Re-attach to the originating distributed trace so handler work shows under the
                // request that raised the event, across the async outbox boundary.
                using var activity = StartDeliveryActivity(entry.TraceParent);

                // Restore the originating tenant so tenant-scoped EF queries inside handlers
                // resolve the correct filter instead of the worker's null tenant.
                // ⚠️ TenantScope, not IMutableTenantContext.SetTenant. Five workers restored the tenant by
                // hand, each resolving the mutable context from its own scope, while the mechanism the
                // documentation points at had zero callers in the framework — because it could not be
                // called: it lived in Pragmatic.MultiTenancy and these modules only reference
                // Abstractions. Moving it beside the contract it serves is what made one way possible.
                // The registered ITenantContext is AmbientTenantContext: it prefers the request tenant
                // and falls back to this ambient one, and outside a request there is no request tenant.
                using (entry.TenantId is { Length: > 0 } entryTenant
                           ? TenantScope.BeginScope(entryTenant)
                           : null)
                {
                    await dispatcher.DispatchAsync([domainEvent], ct).ConfigureAwait(false);
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

    /// <summary>
    ///     Starts a delivery span, parented to the W3C trace captured at write time when present, so a
    ///     distributed trace spans the asynchronous outbox boundary. Returns null when no listener is
    ///     registered (zero overhead) — the dispatch still runs.
    /// </summary>
    private static Activity? StartDeliveryActivity(string? traceParent)
        => !string.IsNullOrEmpty(traceParent) && ActivityContext.TryParse(traceParent, null, out var parent)
            ? ActivitySource.StartActivity("EventOutbox.Deliver", ActivityKind.Consumer, parent)
            : ActivitySource.StartActivity("EventOutbox.Deliver", ActivityKind.Consumer);
}
