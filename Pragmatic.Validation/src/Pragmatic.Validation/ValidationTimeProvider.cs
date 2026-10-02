namespace Pragmatic.Validation;

/// <summary>
///     Provides a configurable <see cref="TimeProvider" /> for date-based validation attributes.
/// </summary>
/// <remarks>
///     <para>
///         Validation attributes (<see cref="Attributes.FutureDateAttribute" />,
///         <see cref="Attributes.PastDateAttribute" />) cannot use DI because they are
///         instantiated by the runtime as attribute instances. This static accessor provides
///         testability without DI coupling.
///     </para>
///     <para>
///         In tests, override <see cref="Current" /> to control the clock:
///         <code>
/// ValidationTimeProvider.Current = new FakeTimeProvider(fixedUtcNow);
/// try { /* run validation tests */ }
/// finally { ValidationTimeProvider.Current = TimeProvider.System; }
/// </code>
///     </para>
/// </remarks>
public static class ValidationTimeProvider
{
    // volatile ensures that writes from one thread are immediately visible to other threads.
    // For parallel test isolation use AsyncLocal<TimeProvider?> scope helpers instead of
    // directly setting Current, which is intentionally process-wide.
    private static volatile TimeProvider _current = TimeProvider.System;

    /// <summary>
    ///     Gets or sets the current time provider used by date validation attributes.
    ///     Defaults to <see cref="TimeProvider.System" />.
    /// </summary>
    /// <remarks>
    ///     This property is process-wide. For isolated parallel test scenarios prefer
    ///     creating a scope that saves and restores the previous value:
    ///     <code>
    /// var previous = ValidationTimeProvider.Current;
    /// ValidationTimeProvider.Current = fakeProvider;
    /// try { /* test */ }
    /// finally { ValidationTimeProvider.Current = previous; }
    ///     </code>
    /// </remarks>
    public static TimeProvider Current
    {
        get => _current;
        set => _current = value ?? TimeProvider.System;
    }
}
