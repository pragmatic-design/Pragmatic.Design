namespace Pragmatic.Audit;

/// <summary>
///     Reads and verifies the audit trail.
/// </summary>
/// <remarks>
///     Separate from <see cref="IAuditTrail" /> because the two have different audiences and different
///     risks: nearly everything writes, very little should read, and reading is what an authorization
///     policy needs to be able to gate on its own.
/// </remarks>
public interface IAuditTrailReader
{
    /// <summary>Returns entries matching the query, newest first.</summary>
    Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken ct = default);

    /// <summary>
    ///     Recomputes the hashes of every sealed segment in the range and reports whether the chain
    ///     still holds.
    /// </summary>
    Task<IntegrityReport> VerifyAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken ct = default);
}
