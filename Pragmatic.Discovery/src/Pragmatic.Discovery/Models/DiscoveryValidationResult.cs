// Pragmatic.Discovery - Validation Result

namespace Pragmatic.Discovery.Models;

/// <summary>The outcome of a topology validation pass.</summary>
public sealed record DiscoveryValidationResult
{
    /// <summary>Returns a result with no issues — always valid.</summary>
    public static DiscoveryValidationResult Ok() => new() { IsValid = true };

    /// <summary>True when there are no Error-severity issues.</summary>
    public bool IsValid { get; init; }

    /// <summary>All issues found (Info, Warning, Error).</summary>
    public IReadOnlyList<DiscoveryValidationIssue> Issues { get; init; } = [];

    /// <summary>Only Error-severity issues.</summary>
    public IEnumerable<DiscoveryValidationIssue> Errors =>
        Issues.Where(i => i.Severity == IssueSeverity.Error);

    /// <summary>Only Warning-severity issues.</summary>
    public IEnumerable<DiscoveryValidationIssue> Warnings =>
        Issues.Where(i => i.Severity == IssueSeverity.Warning);
}
