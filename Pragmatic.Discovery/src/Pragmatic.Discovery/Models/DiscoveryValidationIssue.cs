// Pragmatic.Discovery - Validation Issue

namespace Pragmatic.Discovery.Models;

/// <summary>A single validation issue found during topology validation.</summary>
public sealed record DiscoveryValidationIssue
{
    /// <summary>Issue code (e.g., "DISC001").</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable description of the issue.</summary>
    public required string Message { get; init; }

    /// <summary>Severity level.</summary>
    public IssueSeverity Severity { get; init; }
}
