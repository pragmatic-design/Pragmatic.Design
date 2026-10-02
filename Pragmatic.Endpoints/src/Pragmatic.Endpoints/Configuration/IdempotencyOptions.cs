namespace Pragmatic.Endpoints.Configuration;

/// <summary>
///     Global defaults for [Idempotent] endpoints.
/// </summary>
public sealed class IdempotencyOptions
{
    /// <summary>Header carrying the idempotency key. Default: "Idempotency-Key".</summary>
    public string HeaderName { get; set; } = "Idempotency-Key";

    /// <summary>Default replay window when [Idempotent] does not specify one. Default: 1 hour.</summary>
    public int DefaultDurationSeconds { get; set; } = 3600;
}
