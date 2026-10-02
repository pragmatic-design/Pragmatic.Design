namespace Pragmatic.Temporal.Types;

/// <summary>
///     Policy for handling ambiguous local times that occur during DST fall back.
/// </summary>
/// <remarks>
///     Example: In Europe/Rome on October 27, 2024, clocks fall back from 03:00 to 02:00.
///     The time 02:30 exists twice: once in daylight time (+02:00) and once in standard time (+01:00).
/// </remarks>
public enum AmbiguousTimePolicy
{
    /// <summary>
    ///     Use standard time (the later occurrence, after DST ends).
    ///     02:30 → 02:30 +01:00
    /// </summary>
    UseStandardTime,

    /// <summary>
    ///     Use daylight time (the earlier occurrence, before DST ends).
    ///     02:30 → 02:30 +02:00
    /// </summary>
    UseDaylightTime,

    /// <summary>
    ///     Throw an <see cref="AmbiguousTimeException" />.
    ///     Use this when you want to force the caller to handle the edge case explicitly.
    /// </summary>
    ThrowException
}
