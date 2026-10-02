using System.Data.Common;
using Pragmatic.Audit;
using Pragmatic.Audit.AdoNet;

namespace Pragmatic.Configuration.Database.Audit;

/// <summary>
///     Records configuration and secret changes on the framework's audit trail.
/// </summary>
/// <remarks>
///     <para>
///         The module keeps no audit table of its own. Such a table cannot be verified, and keeping old
///         and new values verbatim makes masking depend on a key being classified <c>[Sensitive]</c>, so
///         anything nobody thought to mark would be stored in the clear in a table nothing treats as
///         secret.
///     </para>
///     <para>
///         <b>The previous value is kept as a hash, and cannot be read back.</b> That is a real loss for
///         operations — "what was this set to before?" has no answer — and it is chosen knowingly: the
///         trail does not retain values it did not choose and cannot classify. A hash
///         still answers the question anyone auditing actually asks, which is whether a specific value
///         was the one in place.
///     </para>
///     <para>
///         Writes go on the caller's transaction, so a configuration change cannot commit without its
///         audit row.
///     </para>
/// </remarks>
internal sealed class ConfigurationAuditRecorder(AdoNetAuditTrail trail)
{
    /// <summary>
    ///     Records a change to a configuration key or a secret, on the caller's transaction.
    /// </summary>
    /// <remarks>
    ///     <paramref name="entityType" /> is <c>config</c> or <c>secret</c>;
    ///     <paramref name="previousValue" /> is hashed and never stored, and callers pass a placeholder
    ///     rather than the value for anything secret.
    /// </remarks>
    public Task RecordAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string entityType,
        string entityKey,
        string? tenantId,
        string action,
        string? previousValue,
        string? changedBy,
        CancellationToken ct = default)
        => trail.RecordAsync(
            new AuditEntry
            {
                SegmentId = string.Empty,   // assigned by the preparer
                Category = AuditCategory.Configuration,
                // A constant per shape, not assembled from the action: "Configuration.Updated" can be
                // found by searching the source, "Configuration." + action cannot.
                Operation = OperationFor(entityType, action),
                ActorRef = changedBy,
                TenantId = tenantId,
                TargetType = entityType,
                TargetId = entityKey,
                Outcome = AuditOutcome.Success,
                ValueHash = AuditValueHash.Of(previousValue),
            },
            connection,
            transaction,
            ct);

    private static string OperationFor(string entityType, string action)
        => (entityType, action) switch
        {
            ("config", "created") => "Configuration.KeyCreated",
            ("config", "updated") => "Configuration.KeyUpdated",
            ("config", "deleted") => "Configuration.KeyDeleted",
            ("secret", "set") => "Configuration.SecretSet",
            ("secret", "deleted") => "Configuration.SecretDeleted",
            _ => throw new ArgumentException(
                $"No audit operation is defined for '{entityType}'/'{action}'. Add one rather than " +
                "letting an entry be written under a name nobody can search for.", nameof(action)),
        };
}
