namespace Pragmatic.Audit;

/// <summary>
///     The one way to write to the audit trail.
/// </summary>
/// <remarks>
///     <b>There is no update and no delete, and their absence is the contract.</b> A trail whose entries
///     can be changed proves nothing, so the operations that would change them are not offered — not
///     even to be refused at runtime. Bounding growth is the sealing service's job, and it discards
///     whole sealed segments rather than touching entries.
/// </remarks>
public interface IAuditTrail
{
    /// <summary>
    ///     Appends an entry. <see cref="AuditEntry.Detail" /> is redacted on the way in; the caller does
    ///     not get to opt out.
    /// </summary>
    ValueTask RecordAsync(AuditEntry entry, CancellationToken ct = default);
}
