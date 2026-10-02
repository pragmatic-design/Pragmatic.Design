namespace Pragmatic.Endpoints.Idempotency;

/// <summary>
///     Captured HTTP response replayed for [Idempotent] retries.
///     Headers carries the selected response headers worth replaying (e.g. Location for 201).
/// </summary>
public sealed record IdempotentResponse(
    int StatusCode,
    string? ContentType,
    byte[] Body,
    Dictionary<string, string>? Headers);
