namespace Pragmatic.Privacy;

/// <summary>
///     Whether an operation reads personal data or writes it.
/// </summary>
/// <remarks>
///     The distinction the register needs and the audit trail does not have: writes are recorded by
///     <c>AuditLogInterceptor</c> on every path, reads are recorded nowhere. Knowing at compile time
///     which operations read personal data is what makes it possible to decide, later and deliberately,
///     which of them deserve a run-time record — instead of choosing between logging every read and
///     logging none.
/// </remarks>
public enum ProcessingAccess
{
    /// <summary>The operation reads the data.</summary>
    Read,

    /// <summary>The operation creates, changes or removes it.</summary>
    Write
}
