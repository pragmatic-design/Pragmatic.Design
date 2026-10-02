namespace Pragmatic.Temporal.Clock;

/// <summary>
///     Abstraction for current time, wrapping TimeProvider for .NET 8+ interop.
///     Use this instead of DateTime.Now/UtcNow for testability.
/// </summary>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Singleton)]
public interface IClock
{
    /// <summary>Gets the current UTC time.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Gets the current local time (server timezone).</summary>
    DateTimeOffset Now { get; }

    /// <summary>Gets the current UTC date.</summary>
    DateOnly UtcToday { get; }

    /// <summary>Gets the current local date.</summary>
    DateOnly Today { get; }

    /// <summary>Gets the current UTC time of day.</summary>
    TimeOnly UtcTimeOfDay { get; }

    /// <summary>Gets the current local time of day.</summary>
    TimeOnly TimeOfDay { get; }

    /// <summary>Returns the underlying TimeProvider for interop with .NET 8+ APIs.</summary>
    TimeProvider GetTimeProvider();
}
