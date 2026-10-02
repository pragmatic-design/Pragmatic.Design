namespace Pragmatic.Configuration;

/// <summary>
///     A secret value together with optional rotation/expiry metadata.
/// </summary>
/// <param name="Value">
///     The secret value, or <c>null</c> when the secret was not found.
/// </param>
/// <param name="ExpiresAt">
///     Absolute UTC instant after which the cached value should no longer be trusted and must be
///     re-fetched. <c>null</c> means the store advertises no expiry. Callers that cache secrets
///     <b>must</b> honor this: treat a value as stale once the current time passes
///     <see cref="ExpiresAt"/>.
/// </param>
/// <param name="RotatedAt">
///     UTC instant at which this secret version was last rotated, when the backing store exposes
///     it. <c>null</c> when unknown. Useful for observability and for callers that want to detect
///     a rotation since they last read the value.
/// </param>
public sealed record SecretEntry(
    string? Value,
    DateTimeOffset? ExpiresAt = null,
    DateTimeOffset? RotatedAt = null)
{
    /// <summary>
    ///     <c>true</c> when a value was found (regardless of expiry).
    /// </summary>
    public bool Found => Value is not null;

    /// <summary>
    ///     Returns <c>true</c> when <see cref="ExpiresAt"/> is set and has already passed relative
    ///     to <paramref name="now"/> (defaults to <see cref="DateTimeOffset.UtcNow"/>).
    /// </summary>
    /// <param name="now">The reference instant to compare against; defaults to the current UTC time.</param>
    /// <returns><c>true</c> if the secret has an expiry that is at or before <paramref name="now"/>.</returns>
    public bool IsExpired(DateTimeOffset? now = null)
        => ExpiresAt is { } exp && (now ?? DateTimeOffset.UtcNow) >= exp;

    /// <summary>A not-found entry with no metadata.</summary>
    public static readonly SecretEntry NotFound = new(Value: (string?)null);
}
