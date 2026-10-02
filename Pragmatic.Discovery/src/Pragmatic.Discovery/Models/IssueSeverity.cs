// Pragmatic.Discovery - Issue Severity

namespace Pragmatic.Discovery.Models;

/// <summary>Severity of a discovery validation issue.</summary>
public enum IssueSeverity
{
    /// <summary>Informational — does not prevent startup.</summary>
    Info,

    /// <summary>Warning — unexpected but non-blocking.</summary>
    Warning,

    /// <summary>Error — prevents coherent operation; blocks startup when <c>ThrowOnValidationFailure</c> is true.</summary>
    Error
}
