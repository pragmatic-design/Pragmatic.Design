namespace Pragmatic.Audit;

/// <summary>How the recorded operation ended.</summary>
public enum AuditOutcome
{
    /// <summary>It happened.</summary>
    Success = 0,

    /// <summary>It was refused — a permission check, a policy, a legal hold.</summary>
    Denied = 1,

    /// <summary>It was attempted and failed.</summary>
    Failed = 2
}
