namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     A controllable TimeProvider for testing timestamp-dependent behavior.
///     Allows setting and advancing time deterministically.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    /// <summary>
    ///     Creates a FakeTimeProvider set to the specified time.
    ///     Defaults to 2024-06-15T10:30:00Z if not specified.
    /// </summary>
    public FakeTimeProvider(DateTimeOffset? initialTime = null)
    {
        _utcNow = initialTime ?? new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _utcNow;

    /// <summary>
    ///     Sets the current time to a specific value.
    /// </summary>
    public void SetUtcNow(DateTimeOffset value) => _utcNow = value;

    /// <summary>
    ///     Advances the current time by the specified duration.
    /// </summary>
    public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
}
