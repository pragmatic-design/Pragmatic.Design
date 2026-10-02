namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Makes the endpoint idempotent: requests must carry an idempotency-key header
///     (default <c>Idempotency-Key</c>, 400 when missing) and successful (2xx) responses
///     are cached and replayed for retries with the same key and body.
/// </summary>
/// <remarks>
///     <para>
///         Backed by Pragmatic.Caching (<c>ICacheStack</c>, category
///         <c>CacheCategories.Idempotency</c>). Requires <c>AddPragmaticCaching()</c> — the filter
///         fails fast when the cache is missing, because silently losing the idempotency guarantee is
///         a correctness bug.
///     </para>
///     <para>
///         <b>A duplicate that arrives while the first request is still running receives 409</b>
///         (<c>code: idempotency_key_in_use</c>, with <c>retryAfter</c>), and its retry receives the
///         stored response once the first has finished. ⚠️ Concurrent requests do not execute the
///         endpoint once and share the response: the filter answers 409. Waiting for the first
///         request would still have to answer something when the wait ran out, and the reservation lives an hour — so the refusal is the
///         floor under any sharing, not an alternative to it.
///     </para>
///     <para>
///         The key identifies the caller and the request: tenant and user, method, path, query string,
///         the idempotency header and a hash of the body. Reusing one key with a different payload,
///         a different query string, or from a different caller is a distinct request and executes.
///         Failures (4xx/5xx) are not cached.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class IdempotentAttribute : Attribute
{
    /// <summary>Replay window in seconds; 0 uses IdempotencyOptions.DefaultDurationSeconds.</summary>
    public int DurationSeconds { get; set; }

    /// <summary>Header carrying the idempotency key; null uses IdempotencyOptions.HeaderName.</summary>
    public string? HeaderName { get; set; }
}
