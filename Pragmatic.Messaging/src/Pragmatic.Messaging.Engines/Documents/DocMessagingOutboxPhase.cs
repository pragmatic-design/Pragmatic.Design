using Pragmatic.Identity;
using Pragmatic.Messaging.Entities;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Serialization;

namespace Pragmatic.Messaging.Engines.Documents;

/// <summary>
///     The Messaging outbox's phase of a <see cref="DocUnitOfWork" />'s save, registered for one boundary (<c>[EnableOutbox]</c>): the events of
///     the entities the save writes become <see cref="OutboxMessageDocument" />s written through the save's session, and leave the entities, so the
///     unit of work dispatches none of them in process after the save.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Atomic with the entities only where the save is: in the save's MongoDB transaction when the server runs them (a replica set). On a
///         standalone server and on Cosmos DB the documents are written one by one, these after the entities: a failure in between leaves the
///         entities written and their events lost, as the documents engine declares for a save there.
///     </para>
///     <para>
///         ⚠️ The phase sees the entities the save writes: an entity that raised an event and changed no field is not among them, and its events
///         go out in process after the save, as without the outbox. EF Core's interceptor reads every tracked entity.
///     </para>
/// </remarks>
public sealed class DocMessagingOutboxPhase(ITenantContext? tenant, ICurrentUser? currentUser, PragmaticJsonOptions? json) : IDocSavePhase
{
    /// <inheritdoc />
    public async ValueTask ApplyAsync(DocSaveContext save, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(save);
        foreach (var message in OutboxMessageCapture.Take(save.Entries.Select(e => e.Entity), save.Now, tenant, currentUser, json))
            await save.Session.InsertAsync(OutboxMessageRecords.From(new OutboxMessageDocument(), message), ct).ConfigureAwait(false);
    }
}
