using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Temporal.Clock;

/// <summary>
///     Production implementation of <see cref="IClock" /> using <see cref="TimeProvider.System" />.
/// </summary>
public sealed class SystemClock : IClock
{
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Creates a new <see cref="SystemClock" /> using the specified <see cref="TimeProvider" />.
    /// </summary>
    /// <param name="timeProvider">The time provider to use.</param>
    public SystemClock(TimeProvider timeProvider)
    {
        ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <summary>
    ///     Creates a new <see cref="SystemClock" /> using <see cref="TimeProvider.System" />.
    /// </summary>
    public SystemClock() : this(TimeProvider.System)
    {
    }

    /// <summary>
    ///     Gets the default singleton instance using <see cref="TimeProvider.System" />.
    /// </summary>
    public static IClock Instance { get; } = new SystemClock(TimeProvider.System);

    /// <inheritdoc />
    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    /// <inheritdoc />
    public DateTimeOffset Now => _timeProvider.GetLocalNow();

    /// <inheritdoc />
    public DateOnly UtcToday => DateOnly.FromDateTime(UtcNow.UtcDateTime);

    /// <inheritdoc />
    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    /// <inheritdoc />
    public TimeOnly UtcTimeOfDay => TimeOnly.FromDateTime(UtcNow.UtcDateTime);

    /// <inheritdoc />
    public TimeOnly TimeOfDay => TimeOnly.FromDateTime(Now.DateTime);

    /// <inheritdoc />
    public TimeProvider GetTimeProvider()
    {
        return _timeProvider;
    }
}