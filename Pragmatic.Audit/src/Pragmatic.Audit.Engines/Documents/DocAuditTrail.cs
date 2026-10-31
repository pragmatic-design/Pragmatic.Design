using Pragmatic.Persistence.Documents;

namespace Pragmatic.Audit.Engines.Documents;

/// <summary>
///     Appends entries to the trail on the documents engine, through the trail's boundary's session: what EF Core's <c>EfCoreAuditTrail</c>
///     does. Not an <see cref="ITransactionalAuditTrail" />, as on the Sql engine: the session joins no transaction a connection owns.
/// </summary>
public sealed class DocAuditTrail(DocSession session, AuditEntryPreparer preparer) : IAuditTrail
{
    /// <inheritdoc />
    public async ValueTask RecordAsync(AuditEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        preparer.Prepare(entry);
        await DocAuditStore.OpenAsync(session, entry, ct).ConfigureAwait(false);
        await session.InsertAsync(DocAuditStore.Document(entry), ct).ConfigureAwait(false);
    }
}
