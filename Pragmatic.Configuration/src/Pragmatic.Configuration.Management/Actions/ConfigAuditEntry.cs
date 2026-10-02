namespace Pragmatic.Configuration.Management.Actions;

/// <summary>One configuration change, as reported by <see cref="GetConfigAuditLog"/>.</summary>
/// <remarks>
///     There is no old or new value: configuration is audited on the shared trail, which keeps a hash
///     of the previous value and never the value itself. "What was this set to before?" has no answer
///     here, which is a real loss for operations and is accepted knowingly — an append-only record kept for years is the wrong place
///     to hold values nobody classified.
///     <para>
///         <c>PreviousValueHash</c> still answers the question an audit actually asks: given a
///         candidate, was that the value in place?
///     </para>
/// </remarks>
public sealed record ConfigAuditEntry(
    string EntityType,
    string EntityKey,
    string? TenantId,
    string Operation,
    byte[]? PreviousValueHash,
    string? ChangedBy,
    DateTimeOffset ChangedAt);
