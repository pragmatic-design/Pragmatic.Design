namespace Pragmatic.Audit;

/// <summary>
///     Removes anything that must not enter the trail from an entry's free-text detail.
/// </summary>
/// <remarks>
///     <para>
///         This is required, not optional, and <see cref="IAuditTrail" /> applies it on the caller's
///         behalf. The trail's central promise is that an entry holds no personal value — that is what
///         allows an erased subject's records to stay verifiable while ceasing to be linkable. A single
///         un-redacted detail string breaks it, and nothing downstream can repair it, because by then
///         the value is in an append-only store.
///     </para>
///     <para>
///         The default implementation covers common shapes. A deployment with its own notion of
///         sensitive text should replace it — and, ideally, the framework should converge on one
///         redactor rather than two (see the module design note).
///     </para>
/// </remarks>
public interface IAuditDetailRedactor
{
    /// <summary>Returns the detail with sensitive content replaced.</summary>
    string Redact(string detail);
}
