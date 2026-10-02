namespace Pragmatic.Endpoints.Samples.Models;

/// <summary>
///     Response from the Hello endpoint.
/// </summary>
public sealed record HelloResponse
{
    /// <summary>
    ///     The greeting message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    ///     The timestamp of the response.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }
}