using System.Data.Common;

namespace Pragmatic.Audit;

/// <summary>
///     Appends an entry inside a transaction the caller already owns, so the change and its audit record
///     commit or roll back together.
/// </summary>
/// <remarks>
///     <para>
///         Separate from <see cref="IAuditTrail" /> because most callers neither have a transaction nor
///         should be made to think about one. This exists for the producers that already guarantee
///         atomicity and would be silently downgraded by anything weaker: a configuration change that
///         commits without its audit row, or an entity written by the persistence interceptor whose
///         trail entry never arrives.
///     </para>
///     <para>
///         <b>The cost is real and is not hidden.</b> Enlisting means the trail writes on the caller's
///         connection, so its tables must live in the same database. An implementation that cannot join
///         the transaction is required to throw rather than write outside it — an entry that commits
///         independently of the change it describes is worse than the missing feature, because the trail
///         would then disagree with the data while looking correct.
///     </para>
/// </remarks>
public interface ITransactionalAuditTrail
{
    /// <summary>
    ///     Appends an entry on <paramref name="transaction" />. Redaction applies exactly as it does on
    ///     <see cref="IAuditTrail.RecordAsync" />.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     The trail cannot join this transaction — typically because its store is on a different
    ///     connection. Never silently degraded to an independent write.
    /// </exception>
    ValueTask RecordAsync(AuditEntry entry, DbTransaction transaction, CancellationToken ct = default);
}
