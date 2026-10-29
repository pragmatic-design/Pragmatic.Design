using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Events.Outbox;
using Pragmatic.Serialization;

namespace Pragmatic.Events.Engines;

/// <summary>
///     The part of a delivery pass no engine changes: the claim's token and lease, then each claimed row delivered
///     (<see cref="EventOutboxDelivery.DeliverAsync" />) and marked as EF Core's drainer marks an entry: delivered, or its attempt counted, its
///     error kept and its claim released for a later pass. The engine claims, loads and writes the rows back.
/// </summary>
internal static partial class EventOutboxPass
{
    /// <summary>A token unique to one pass: the claim stamps it, and the pass loads only the rows that carry it.</summary>
    public static string ClaimToken() => $"{Environment.MachineName}:{Guid.NewGuid():N}";

    /// <summary>Delivers <paramref name="rows" /> with the scope's dispatcher, allowlist and JSON options; returns how many were delivered.</summary>
    public static async Task<int> DeliverAsync(IEnumerable<IEventOutboxRecord> rows, IServiceProvider services, TimeProvider time, ILogger logger, CancellationToken ct)
    {
        var dispatcher = services.GetRequiredService<IDomainEventDispatcher>();
        var types = services.GetRequiredService<IEventOutboxTypeResolver>();
        var options = (services.GetService<PragmaticJsonOptions>() ?? PragmaticJsonOptions.Default).Build();
        var delivered = 0;
        foreach (var row in rows)
        {
            try
            {
                var refused = await EventOutboxDelivery.DeliverAsync(row.EventType, row.Payload, row.TenantId, row.TraceParent, types, options, dispatcher, ct).ConfigureAwait(false);
                if (refused is not null)
                {
                    Failed(row, refused);
                    LogSkipped(logger, row.Id, row.EventType, refused);
                    continue;
                }

                row.ProcessedAt = time.GetUtcNow().UtcTicks;
                delivered++;
            }
            catch (Exception failure)
            {
                // The whole chain, cut to what a narrow column holds: the row records the failure and the pass goes on to the next.
                var text = failure.ToString();
                Failed(row, text.Length <= 2000 ? text : text[..2000]);
                LogFailed(logger, failure, row.Id);
            }
        }

        return delivered;
    }

    private static void Failed(IEventOutboxRecord row, string error)
    {
        row.Attempts++;
        row.LastError = error;
        row.ClaimedBy = null; // released: a later pass takes it again, within MaxAttempts
        row.ClaimedUntil = null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped outbox event {OutboxEntryId} of type {EventType}: {Reason}")]
    private static partial void LogSkipped(ILogger logger, Guid outboxEntryId, string eventType, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to deliver outbox event {OutboxEntryId}")]
    private static partial void LogFailed(ILogger logger, Exception failure, Guid outboxEntryId);
}
