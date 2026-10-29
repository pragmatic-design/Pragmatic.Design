using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Sql;
using Pragmatic.Serialization;

namespace Pragmatic.Events.Engines.Sql;

/// <summary>
///     The event outbox's phase of a <see cref="SqlUnitOfWork" />'s save, registered for one boundary (<c>[EnableEventOutbox]</c>): the events of
///     the entities the save writes become <see cref="EventOutboxRow" />s in the save's batch, so they commit or roll back with the entities, and
///     leave the entities, so the unit of work dispatches none of them in process after the commit.
/// </summary>
/// <remarks>
///     ⚠️ The phase sees the entities the save writes: an entity that raised an event and changed no column is not among them, and its events go
///     out in process after the commit, as without the outbox. EF Core's interceptor reads every tracked entity.
/// </remarks>
public sealed class SqlEventOutboxPhase(ITenantContext? tenant, PragmaticJsonOptions? json) : ISqlSavePhase
{
    /// <inheritdoc />
    public ValueTask ApplyAsync(SqlSaveContext save, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(save);
        foreach (var captured in EventOutboxCapture.Take(save.Entries.Select(e => e.Entity), save.Now, tenant?.TenantId, json))
        {
            save.Batch.InsertAsIs(new EventOutboxRow
            {
                Id = captured.Id, EventType = captured.EventType, Payload = captured.Payload, OccurredAt = captured.OccurredAt,
                TenantId = captured.TenantId, TraceParent = captured.TraceParent, CreatedAt = captured.CreatedAt,
            });
        }

        return ValueTask.CompletedTask;
    }
}
