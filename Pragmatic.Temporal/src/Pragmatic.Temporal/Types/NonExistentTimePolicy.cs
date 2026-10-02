namespace Pragmatic.Temporal.Types;

/// <summary>
///     Policy for handling non-existent local times that occur during DST spring forward.
/// </summary>
/// <remarks>
///     Example: In Europe/Rome on March 31, 2024, clocks jump from 02:00 to 03:00.
///     The time 02:30 does not exist on that day.
/// </remarks>
public enum NonExistentTimePolicy
{
    /// <summary>
    ///     Shift to the next valid instant (the start of DST).
    ///     02:30 → 03:00 (first valid time after the gap)
    /// </summary>
    ShiftForward,

    /// <summary>
    ///     Throw an <see cref="InvalidOperationException" />.
    ///     Use this when you want to force the caller to handle the edge case explicitly.
    /// </summary>
    ThrowException
}
