using System.Text.Json.Serialization;

namespace Pragmatic.ControlPlane;

/// <summary>
///     Health snapshot from a single <see cref="IHostHealthContributor"/>.
/// </summary>
public sealed record ContributorHealthReport
{
    /// <summary>Current status.</summary>
    public required ContributorHealthStatus Status { get; init; }

    /// <summary>Human-readable description (e.g. "Connected, 12ms latency").</summary>
    public string? Message { get; init; }

    /// <summary>Structured data for dashboards. Values should be JSON-serializable primitives.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? Data { get; init; }

    /// <summary>When this report was generated.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Shorthand for a healthy report.</summary>
    /// <param name="message">Optional human-readable description.</param>
    /// <returns>A report with <see cref="Status"/> set to healthy.</returns>
    public static ContributorHealthReport Healthy(string? message = null)
        => new() { Status = ContributorHealthStatus.Healthy, Message = message };

    /// <summary>Shorthand for a degraded report.</summary>
    /// <param name="message">Human-readable description of the degradation.</param>
    /// <param name="data">Optional structured data for dashboards.</param>
    /// <returns>A report with <see cref="Status"/> set to degraded.</returns>
    public static ContributorHealthReport Degraded(string message, Dictionary<string, object>? data = null)
        => new() { Status = ContributorHealthStatus.Degraded, Message = message, Data = data };

    /// <summary>Shorthand for an unhealthy report.</summary>
    /// <param name="message">Human-readable description of the failure.</param>
    /// <param name="data">Optional structured data for dashboards.</param>
    /// <returns>A report with <see cref="Status"/> set to unhealthy.</returns>
    public static ContributorHealthReport Unhealthy(string message, Dictionary<string, object>? data = null)
        => new() { Status = ContributorHealthStatus.Unhealthy, Message = message, Data = data };
}
