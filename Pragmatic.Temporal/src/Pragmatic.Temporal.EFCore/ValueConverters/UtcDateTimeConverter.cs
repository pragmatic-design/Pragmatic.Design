using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;

/// <summary>
///     Stores a <see cref="DateTime" /> as UTC, and labels what comes back.
/// </summary>
/// <remarks>
///     <para>
///         A <see cref="DateTimeKind.Local" /> value names a different instant, so it is converted. An
///         <see cref="DateTimeKind.Unspecified" /> one is taken at its word and labelled UTC.
///     </para>
///     <para>
///         ⚠️ Unspecified deliberately does not go through <c>ToUniversalTime()</c>, which reads it as
///         local: a value already in UTC would then be shifted by the machine's offset, differently on
///         every machine. This is the same rule the JSON layer applies on the way out, so the two ends
///         of the system agree.
///     </para>
///     <para>
///         Reading labels the value UTC because most providers return <c>Unspecified</c>, and a caller
///         comparing kinds would otherwise see a value that does not say what it is.
///     </para>
/// </remarks>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    /// <summary>Creates a new instance.</summary>
    public UtcDateTimeConverter()
        : base(
            value => value.Kind == DateTimeKind.Local
                ? value.ToUniversalTime()
                : DateTime.SpecifyKind(value, DateTimeKind.Utc),
            stored => DateTime.SpecifyKind(stored, DateTimeKind.Utc))
    {
    }
}
