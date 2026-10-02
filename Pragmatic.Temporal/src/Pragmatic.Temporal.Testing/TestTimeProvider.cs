namespace Pragmatic.Temporal.Testing;

/// <summary>
///     TimeProvider adapter for TestClock.
/// </summary>
internal sealed class TestTimeProvider(TestClock clock) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        return clock.UtcNow;
    }
}
