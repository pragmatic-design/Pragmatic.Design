namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Model representing rate limiting configuration.
/// </summary>
internal sealed record RateLimitModel
{
    /// <summary>
    ///     Named policy to use.
    /// </summary>
    public string? Policy { get; init; }

    /// <summary>
    ///     Maximum requests in the window.
    /// </summary>
    public int Requests { get; init; }

    /// <summary>
    ///     Time window (e.g., "1m", "1h").
    /// </summary>
    public string? Window { get; init; }
}
