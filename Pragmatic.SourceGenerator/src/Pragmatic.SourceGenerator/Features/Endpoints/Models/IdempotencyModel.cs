namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     [Idempotent] configuration for an endpoint.
/// </summary>
internal sealed record IdempotencyModel
{
    /// <summary>Replay window in seconds; 0 uses the runtime IdempotencyOptions default.</summary>
    public int DurationSeconds { get; init; }

    /// <summary>Header carrying the idempotency key; null uses the runtime IdempotencyOptions default.</summary>
    public string? HeaderName { get; init; }
}
